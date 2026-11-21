package com.arthostudio.arthoai;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONArray;
import org.json.JSONObject;

final class AndroidUpdater {
    private static final String REPO = "BlackGen3ral/soneca";
    private static final String API = "https://api.github.com/repos/" + REPO + "/releases";
    private static final String DOWNLOADS = "https://github.com/" + REPO + "/releases/download/";
    private static final Pattern VERSION = Pattern.compile("(0|[1-9]\\d{0,7})\\.(0|[1-9]\\d{0,7})\\.(0|[1-9]\\d{0,7})");
    private static final int PAGE_SIZE = 30;
    private static final long MAX_APK_SIZE = 150L * 1024 * 1024;

    static Release check(String installedVersion) throws Exception {
        Release newest = null;
        String highestVersion = installedVersion;
        for (int page = 1; ; page++) {
            HttpsURLConnection connection = open(new URL(API + "?per_page=" + PAGE_SIZE + "&page=" + page));
            JSONArray releases;
            try {
                connection.setRequestProperty("Accept", "application/vnd.github+json");
                try (InputStream input = connection.getInputStream();
                     ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    transfer(input, output, null, 5L * 1024 * 1024);
                    releases = new JSONArray(output.toString(StandardCharsets.UTF_8.name()));
                }
            } finally {
                connection.disconnect();
            }
            for (int i = 0; i < releases.length(); i++) {
                JSONObject item = releases.getJSONObject(i);
                String tag = item.optString("tag_name");
                if (item.optBoolean("draft") || !item.optBoolean("prerelease") || !tag.startsWith("android-v")) continue;
                String version = tag.substring("android-v".length());
                if (!VERSION.matcher(version).matches() || compare(version, highestVersion) <= 0) continue;
                Release candidate = release(item.getJSONArray("assets"), tag, version);
                if (candidate != null) {
                    newest = candidate;
                    highestVersion = version;
                }
            }
            if (releases.length() < PAGE_SIZE) return newest;
        }
    }

    private static Release release(JSONArray assets, String tag, String version) throws Exception {
        String name = "ArthoAI-Android-" + version + ".apk";
        String expectedUrl = DOWNLOADS + tag + "/" + name;
        for (int i = 0; i < assets.length(); i++) {
            JSONObject asset = assets.getJSONObject(i);
            String digest = asset.optString("digest");
            long size = asset.optLong("size");
            if (name.equals(asset.optString("name")) && expectedUrl.equals(asset.optString("browser_download_url"))
                    && "uploaded".equals(asset.optString("state")) && digest.matches("sha256:[0-9a-fA-F]{64}")
                    && size > 0 && size <= MAX_APK_SIZE) {
                return new Release(version, new URL(expectedUrl), digest.substring(7), size);
            }
        }
        return null;
    }

    static void download(Context context, Release release) throws Exception {
        File temporary = File.createTempFile("artho-update-", ".apk", context.getCacheDir());
        try {
            MessageDigest hash = MessageDigest.getInstance("SHA-256");
            HttpsURLConnection connection = connectAsset(release.url);
            try (InputStream input = connection.getInputStream(); FileOutputStream output = new FileOutputStream(temporary)) {
                if (transfer(input, output, hash, release.size) != release.size) throw new IOException("APK incompleto");
            } finally {
                connection.disconnect();
            }
            StringBuilder actual = new StringBuilder();
            for (byte octet : hash.digest()) actual.append(String.format(Locale.ROOT, "%02x", octet & 255));
            if (!release.sha256.equalsIgnoreCase(actual.toString())) throw new IOException("APK com SHA-256 incorreto");
            verifyPackage(context, temporary, release.version);
            File destination = new File(context.getCacheDir(), UpdateApkProvider.APK_NAME);
            android.system.Os.rename(temporary.getPath(), destination.getPath());
        } finally {
            temporary.delete();
        }
    }

    @SuppressWarnings("deprecation")
    private static void verifyPackage(Context context, File file, String expectedVersion) throws Exception {
        PackageManager manager = context.getPackageManager();
        PackageInfo current = manager.getPackageInfo(context.getPackageName(), PackageManager.GET_SIGNATURES);
        PackageInfo incoming = manager.getPackageArchiveInfo(file.getPath(), PackageManager.GET_SIGNATURES);
        if (incoming == null || !current.packageName.equals(incoming.packageName)
                || !expectedVersion.equals(incoming.versionName) || versionCode(incoming) <= versionCode(current)
                || current.signatures == null || !Arrays.equals(current.signatures, incoming.signatures)) {
            throw new IOException("APK incompatível com a instalação do Artho AI");
        }
    }

    @SuppressWarnings("deprecation")
    private static long versionCode(PackageInfo info) {
        return android.os.Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }

    private static HttpsURLConnection connectAsset(URL url) throws Exception {
        for (int i = 0; i < 5; i++) {
            HttpsURLConnection connection = open(url);
            try {
                int status = connection.getResponseCode();
                if (status == 200) return connection;
                if (status < 300 || status > 399) throw new IOException("GitHub HTTP " + status);
                String location = connection.getHeaderField("Location");
                if (location == null) throw new IOException("GitHub sem destino de download");
                url = new URL(url, location);
            } catch (Exception error) {
                connection.disconnect();
                throw error;
            }
            connection.disconnect();
            if (!"release-assets.githubusercontent.com".equalsIgnoreCase(url.getHost())
                    && !"objects.githubusercontent.com".equalsIgnoreCase(url.getHost())) {
                throw new IOException("Destino de download não autorizado");
            }
        }
        throw new IOException("Redirecionamentos demais");
    }

    private static HttpsURLConnection open(URL url) throws IOException {
        if (!"https".equals(url.getProtocol()) || url.getUserInfo() != null
                || (url.getPort() != -1 && url.getPort() != 443)) throw new IOException("URL insegura");
        HttpsURLConnection connection = (HttpsURLConnection) url.openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(10000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("User-Agent", "ArthoAI-Android");
        return connection;
    }

    private static long transfer(InputStream input, java.io.OutputStream output, MessageDigest hash, long limit)
            throws IOException {
        byte[] buffer = new byte[32768];
        long total = 0;
        int count;
        while ((count = input.read(buffer)) != -1) {
            total += count;
            if (total > limit) throw new IOException("Arquivo maior que o esperado");
            output.write(buffer, 0, count);
            if (hash != null) hash.update(buffer, 0, count);
        }
        return total;
    }

    static int compare(String left, String right) {
        Matcher a = VERSION.matcher(left);
        Matcher b = VERSION.matcher(right);
        if (!a.matches() || !b.matches()) throw new IllegalArgumentException("Versão inválida");
        for (int i = 1; i <= 3; i++) {
            int order = Integer.compare(Integer.parseInt(a.group(i)), Integer.parseInt(b.group(i)));
            if (order != 0) return order;
        }
        return 0;
    }

    static final class Release {
        final String version;
        final URL url;
        final String sha256;
        final long size;

        Release(String version, URL url, String sha256, long size) {
            this.version = version;
            this.url = url;
            this.sha256 = sha256;
            this.size = size;
        }
    }
}
