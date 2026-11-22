package com.arthostudio.arthoai;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Arrays;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import org.json.JSONObject;

final class StatusClient {
    static final class OldPairingProtocol extends java.io.IOException { }

    static JSONObject readStatus(Pairing pairing) throws Exception {
        return request(pairing, "GET", "/v1/status");
    }

    static JSONObject request(Pairing pairing, String method, String path) throws Exception {
        return request(pairing, method, path, null);
    }

    static void enroll(Pairing invitation, Pairing credential) throws Exception {
        request(invitation, "POST", "/v1/pair/complete", credential.token);
    }

    private static JSONObject request(Pairing pairing, String method, String path, String proposal) throws Exception {
        SSLContext tls = SSLContext.getInstance("TLS");
        tls.init(null, new TrustManager[] {new X509TrustManager() {
            @Override public void checkServerTrusted(X509Certificate[] chain, String auth)
                    throws CertificateException {
                if (chain == null || chain.length == 0) throw new CertificateException("Certificado ausente");
                chain[0].checkValidity();
                try {
                    byte[] hash = MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded());
                    if (!MessageDigest.isEqual(hash, pairing.fingerprint)) {
                        throw new CertificateException("Certificado diferente do código");
                    }
                } catch (java.security.NoSuchAlgorithmException error) {
                    throw new CertificateException(error);
                }
            }
            @Override public void checkClientTrusted(X509Certificate[] chain, String auth)
                    throws CertificateException { throw new CertificateException("Cliente não suportado"); }
            @Override public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
        }}, null);
        URL url = new URL("https://" + pairing.host + ":47834" + path);
        HttpsURLConnection connection = (HttpsURLConnection) url.openConnection(java.net.Proxy.NO_PROXY);
        connection.setSSLSocketFactory(tls.getSocketFactory());
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(4500);
        connection.setReadTimeout(12000);
        connection.setRequestMethod(method);
        connection.setRequestProperty("Authorization", "Bearer " + pairing.token);
        if (proposal != null) connection.setRequestProperty("X-Artho-Pair-Key", proposal);
        try {
            if ("POST".equals(method)) {
                connection.setDoOutput(true);
                connection.setFixedLengthStreamingMode(0);
                connection.getOutputStream().close();
            }
            int status = connection.getResponseCode();
            if (status == 401) throw new SecurityException();
            if (status == 404) {
                if (proposal != null) throw new OldPairingProtocol();
                throw new java.io.IOException("No Windows, ative o controle na aba Celular e gere outro QR Code.");
            }
            if (status != 200 && status != 400) throw new java.io.IOException("Resposta inválida");
            try (InputStream input = status == 200 ? connection.getInputStream() : connection.getErrorStream();
                    ByteArrayOutputStream bytes = new ByteArrayOutputStream()) {
                if (input == null) throw new java.io.IOException("Resposta inválida");
                byte[] buffer = new byte[1024];
                int count;
                while ((count = input.read(buffer)) != -1) {
                    if (bytes.size() + count > 8192) throw new java.io.IOException("Resposta muito grande");
                    bytes.write(buffer, 0, count);
                }
                JSONObject result = new JSONObject(new String(bytes.toByteArray(), StandardCharsets.UTF_8));
                if (status != 200) throw new java.io.IOException(result.optString("error", "O Windows recusou a solicitação."));
                return result;
            }
        } finally {
            connection.disconnect();
        }
    }

    static final class Pairing {
        final String host;
        final byte[] fingerprint;
        final String token;
        final boolean durable;

        Pairing(String host, byte[] fingerprint, String token, boolean durable) {
            this.host = host;
            this.fingerprint = fingerprint;
            this.token = token;
            this.durable = durable;
        }

        Pairing generateCredential() {
            byte[] bytes = new byte[32];
            new java.security.SecureRandom().nextBytes(bytes);
            try { return new Pairing(host, fingerprint, hex(bytes, 0), true); }
            finally { Arrays.fill(bytes, (byte) 0); }
        }

        static Pairing parse(String text) {
            if (!text.matches("[A-Za-z0-9_-]{91}")) throw new IllegalArgumentException();
            byte[] value = Base64.decode(text, Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP);
            if (value.length != 68) throw new IllegalArgumentException();
            int a = value[0] & 255, b = value[1] & 255;
            if (!(a == 10 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168))) {
                throw new IllegalArgumentException();
            }
            String host = a + "." + b + "." + (value[2] & 255) + "." + (value[3] & 255);
            String token = hex(value, 36);
            byte[] fingerprint = Arrays.copyOfRange(value, 4, 36);
            Arrays.fill(value, (byte) 0);
            return new Pairing(host, fingerprint, token, false);
        }

        private static String hex(byte[] value, int offset) {
            StringBuilder token = new StringBuilder((value.length - offset) * 2);
            for (int i = offset; i < value.length; i++) {
                token.append("0123456789ABCDEF".charAt((value[i] & 255) >>> 4));
                token.append("0123456789ABCDEF".charAt(value[i] & 15));
            }
            return token.toString();
        }
    }
}
