package com.arthostudio.arthoai;

import android.content.Context;
import android.util.AtomicFile;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;
import javax.net.ssl.HttpsURLConnection;

final class HousePackage {
    private static final int MAX_SIZE = 1_500_000;
    private static final int HEADER = 36;
    private static final byte[] MAGIC = "ARTHOC01".getBytes(StandardCharsets.US_ASCII);
    private final AtomicFile cache;
    private String etag;

    HousePackage(Context context, StatusClient.Pairing pairing) {
        cache = new AtomicFile(new File(context.getFilesDir(), "house-" +
                hash(pairing.token.getBytes(StandardCharsets.US_ASCII)) + ".sealed"));
    }

    boolean exists() { return cache.getBaseFile().exists(); }
    void delete() {
        cache.delete();
        etag = null;
    }

    boolean sync(StatusClient.Pairing pairing) throws Exception {
        HttpsURLConnection connection = StatusClient.open(pairing, "GET", "/v1/house");
        try {
            if (exists()) {
                if (etag == null) {
                    try (InputStream input = cache.openRead()) {
                        etag = "\"" + hash(read(input)) + "\"";
                    } catch (IOException unreadable) { delete(); }
                }
                if (etag != null) connection.setRequestProperty("If-None-Match", etag);
            }
            int code = connection.getResponseCode();
            if (code == 401) {
                delete();
                return false;
            }
            if (code != 200) return false;
            byte[] bytes;
            try (InputStream input = connection.getInputStream()) { bytes = read(input); }
            String receivedTag = "\"" + hash(bytes) + "\"";
            if (!receivedTag.equals(connection.getHeaderField("ETag")))
                throw new IOException("Maquete incompleta.");
            FileOutputStream output = cache.startWrite();
            try {
                output.write(bytes);
                cache.finishWrite(output);
                etag = receivedTag;
                return true;
            } catch (IOException error) {
                cache.failWrite(output);
                throw error;
            }
        } finally {
            connection.disconnect();
        }
    }

    byte[] unlock(char[] password) throws Exception {
        byte[] bytes;
        try (InputStream input = cache.openRead()) { bytes = read(input); }
        PBEKeySpec input = new PBEKeySpec(password, Arrays.copyOfRange(bytes, 8, 24), 600_000, 256);
        byte[] key = null;
        try {
            key = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(input).getEncoded();
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, bytes, 24, 12));
            cipher.updateAAD(bytes, 0, HEADER);
            return cipher.doFinal(bytes, HEADER, bytes.length - HEADER);
        } finally {
            input.clearPassword();
            if (key != null) Arrays.fill(key, (byte) 0);
        }
    }

    private static byte[] read(InputStream input) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = input.read(buffer)) != -1) {
            if (bytes.size() + count > MAX_SIZE) throw new IOException("Maquete muito grande.");
            bytes.write(buffer, 0, count);
        }
        byte[] result = bytes.toByteArray();
        if (result.length <= HEADER + 16 || !Arrays.equals(MAGIC, Arrays.copyOf(result, 8)))
            throw new IOException("Maquete inválida.");
        return result;
    }

    private static String hash(byte[] bytes) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(bytes);
            StringBuilder hex = new StringBuilder(digest.length * 2);
            for (byte value : digest) {
                hex.append("0123456789ABCDEF".charAt((value & 255) >>> 4));
                hex.append("0123456789ABCDEF".charAt(value & 15));
            }
            return hex.toString();
        } catch (java.security.NoSuchAlgorithmException impossible) {
            throw new IllegalStateException(impossible);
        }
    }
}
