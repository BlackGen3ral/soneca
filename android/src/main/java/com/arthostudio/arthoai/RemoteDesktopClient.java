package com.arthostudio.arthoai;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

final class RemoteDesktopClient {
    private final StatusClient.Pairing pairing;
    private final String route;
    private final AtomicBoolean closed = new AtomicBoolean();

    private RemoteDesktopClient(StatusClient.Pairing pairing, UUID id) {
        this.pairing = pairing;
        route = "/v1/desktop/" + id;
    }

    static RemoteDesktopClient connect(StatusClient.Pairing pairing) throws Exception {
        JSONObject result = StatusClient.request(pairing, "POST", "/v1/desktop/open?monitors=1");
        return new RemoteDesktopClient(pairing, UUID.fromString(result.getString("session")));
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
                    connection.getHeaderFieldInt("X-Artho-Pointer-Y", -1),
                    connection.getHeaderField("X-Artho-Monitor"),
                    connection.getHeaderFieldInt("X-Artho-Monitor-Number", 1),
                    connection.getHeaderFieldInt("X-Artho-Monitor-Count", 1));
        } finally { connection.disconnect(); }
    }

    void input(String action, int x, int y, String value, int delta, String monitor, int edge) throws Exception {
        byte[] packet = new JSONObject().put("action", action).put("x", x).put("y", y)
                .put("value", value).put("delta", delta).put("monitor", monitor).put("edge", edge)
                .toString().getBytes(StandardCharsets.UTF_8);
        HttpsURLConnection connection = StatusClient.open(pairing, "POST", route + "/input");
        try {
            connection.setReadTimeout(2500);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json");
            connection.setFixedLengthStreamingMode(packet.length);
            try (OutputStream output = connection.getOutputStream()) { output.write(packet); }
            checkResponse(connection);
            try (InputStream reply = connection.getInputStream()) {
                // Read the small acknowledgment so the HTTPS connection can be reused.
                byte[] buffer = new byte[64];
                int remaining = 512;
                while (remaining > 0) {
                    int count = reply.read(buffer, 0, Math.min(remaining, buffer.length));
                    if (count < 0) break;
                    remaining -= count;
                }
            }
        } finally { connection.disconnect(); }
    }

    void disconnect() {
        if (!closed.compareAndSet(false, true)) return;
        HttpsURLConnection connection = null;
        try {
            connection = StatusClient.open(pairing, "POST", route + "/close");
            connection.setConnectTimeout(1000);
            connection.setReadTimeout(1000);
            connection.setDoOutput(true);
            connection.setFixedLengthStreamingMode(0);
            connection.getOutputStream().close();
            connection.getResponseCode();
        } catch (Exception ignored) {
        } finally {
            if (connection != null) connection.disconnect();
        }
    }

    private static void checkResponse(HttpsURLConnection connection) throws IOException {
        int status = connection.getResponseCode();
        if (status == 401 || status == 403) throw new SecurityException("Vínculo removido. Pareie novamente.");
        if (status == 410 || status == 400) throw new SessionExpired();
        if (status != 200) throw new IOException("Tela indisponível no momento.");
    }

    static final class SessionExpired extends IOException { }

    static final class Frame {
        final Bitmap image;
        final int x, y, monitorNumber, monitorCount;
        final String monitor;
        Frame(Bitmap image, int x, int y, String monitor, int monitorNumber, int monitorCount) {
            this.image = image;
            this.x = x;
            this.y = y;
            this.monitor = monitor;
            this.monitorNumber = monitorNumber;
            this.monitorCount = monitorCount;
        }
    }
}
