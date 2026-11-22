package com.arthostudio.arthoai;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

final class RemoteDesktopClient {
    private final StatusClient.Pairing pairing;
    private final String route;

    private RemoteDesktopClient(StatusClient.Pairing pairing, UUID id) {
        this.pairing = pairing;
        route = "/v1/desktop/" + id;
    }

    static RemoteDesktopClient connect(StatusClient.Pairing pairing) throws Exception {
        try {
            JSONObject result = StatusClient.request(pairing, "POST", "/v1/desktop/open");
            return new RemoteDesktopClient(pairing, UUID.fromString(result.getString("session")));
        } catch (java.io.IOException error) {
            throw new IOException("No PC, abra Celular → Tela remota e libere o acesso.", error);
        }
    }

    Frame frame() throws Exception {
        HttpsURLConnection connection = StatusClient.open(pairing, "GET", route + "/frame");
        connection.setReadTimeout(4000);
        try {
            checkResponse(connection);
            if (!"image/jpeg".equalsIgnoreCase(connection.getContentType())) throw new IOException("Imagem inválida.");
            int size = connection.getContentLength();
            if (size > 2 * 1024 * 1024) throw new IOException("Imagem muito grande.");
            byte[] bytes;
            try (InputStream input = connection.getInputStream();
                 ByteArrayOutputStream output = new ByteArrayOutputStream(size > 0 ? size : 32768)) {
                byte[] block = new byte[8192];
                int count;
                while ((count = input.read(block)) != -1) {
                    if (output.size() + count > 2 * 1024 * 1024) throw new IOException("Imagem muito grande.");
                    output.write(block, 0, count);
                }
                bytes = output.toByteArray();
            }
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.inJustDecodeBounds = true;
            BitmapFactory.decodeByteArray(bytes, 0, bytes.length, options);
            if (options.outWidth < 1 || options.outHeight < 1 || options.outWidth > 1280 || options.outHeight > 1280)
                throw new IOException("Resolução inválida.");
            options.inJustDecodeBounds = false;
            Bitmap bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.length, options);
            if (bitmap == null) throw new IOException("Imagem inválida.");
            return new Frame(bitmap, connection.getHeaderFieldInt("X-Artho-Pointer-X", -1),
                    connection.getHeaderFieldInt("X-Artho-Pointer-Y", -1));
        } finally { connection.disconnect(); }
    }

    void input(String action, int x, int y, String value, int delta) throws Exception {
        byte[] packet = new JSONObject().put("action", action).put("x", x).put("y", y)
                .put("value", value).put("delta", delta).toString().getBytes(StandardCharsets.UTF_8);
        HttpsURLConnection connection = StatusClient.open(pairing, "POST", route + "/input");
        try {
            connection.setReadTimeout(2500);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json");
            connection.setFixedLengthStreamingMode(packet.length);
            try (OutputStream output = connection.getOutputStream()) { output.write(packet); }
            checkResponse(connection);
        } finally { connection.disconnect(); }
    }

    void disconnect() {
        try { StatusClient.request(pairing, "POST", route + "/close"); }
        catch (Exception ignored) { }
    }

    private static void checkResponse(HttpsURLConnection connection) throws IOException {
        if (connection.getResponseCode() != 200)
            throw new IOException("Acesso encerrado. Confira a liberação no Windows.");
    }

    static final class Frame {
        final Bitmap image;
        final int x, y;
        Frame(Bitmap image, int x, int y) { this.image = image; this.x = x; this.y = y; }
    }
}
