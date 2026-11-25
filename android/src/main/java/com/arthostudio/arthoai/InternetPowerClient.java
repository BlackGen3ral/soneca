package com.arthostudio.arthoai;

import java.io.IOException;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Locale;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import javax.net.ssl.HttpsURLConnection;

final class InternetPowerClient {
    private static final String URL_BASE = "https://artho-ai-power.arthurgabrielfera.workers.dev";

    static String requestShutdown(StatusClient.Pairing pairing, long issued) throws Exception {
        switch (send(pairing, issued, true)) {
            case 202: return null;
            case 404: return "PC sem contato ou desligamento pela internet desativado. Confira a aba Celular no Windows.";
            case 409: return "Já existe um pedido recente. Aguarde alguns segundos e confira o PC.";
            case 400: return "Pedido inválido. Confira se a data e a hora do celular estão no modo automático.";
            default: throw new IOException("O serviço de desligamento está indisponível.");
        }
    }

    enum Receipt { WAITING, RECEIVED, EXPIRED }

    static Receipt readReceipt(StatusClient.Pairing pairing, long issued) throws Exception {
        switch (send(pairing, issued, false)) {
            case 200: return Receipt.RECEIVED;
            case 202: return Receipt.WAITING;
            case 410: return Receipt.EXPIRED;
            default: throw new IOException("O status do pedido está indisponível.");
        }
    }

    private static int send(StatusClient.Pairing pairing, long issued, boolean shutdown) throws Exception {
        byte[] key = digest(pairing.token);
        String path = shutdown ? "/v1/phone/shutdown" : "/v1/phone/receipt";
        HttpsURLConnection connection = (HttpsURLConnection) new URL(URL_BASE + path).openConnection();
        try {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(5000);
            connection.setReadTimeout(5000);
            connection.setRequestMethod("POST");
            connection.setRequestProperty("Authorization", "Bearer " + derive(key, "phone"));
            connection.setRequestProperty("X-Artho-Mailbox", hex(digest(derive(key, "pc"))));
            connection.setRequestProperty("X-Artho-Issued", Long.toString(issued));
            if (shutdown) connection.setRequestProperty("X-Artho-Signature", derive(key, "shutdown:" + issued));
            return connection.getResponseCode();
        } finally { connection.disconnect(); }
    }

    private static byte[] digest(String text) throws Exception {
        return MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.US_ASCII));
    }

    private static String derive(byte[] key, String purpose) throws Exception {
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(key, "HmacSHA256"));
        return hex(mac.doFinal(("artho-internet-power-v1:" + purpose).getBytes(StandardCharsets.US_ASCII)));
    }

    private static String hex(byte[] value) {
        StringBuilder result = new StringBuilder(value.length * 2);
        for (byte part : value) result.append(String.format(Locale.ROOT, "%02x", part & 255));
        return result.toString();
    }
}
