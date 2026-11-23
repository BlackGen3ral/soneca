package com.arthostudio.arthoai;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

final class PairingVault {
    private static final String ALIAS = "artho-mobile-credential-v1";
    private static final String ACTIVE = "active";
    private static final String PENDING = "pending";

    static StatusClient.Pairing load(Context context) {
        StatusClient.Pairing pending = read(context, PENDING);
        return pending != null ? pending : read(context, ACTIVE);
    }

    static StatusClient.Pairing pendingFor(Context context, StatusClient.Pairing invitation) {
        StatusClient.Pairing pending = read(context, PENDING);
        return pending != null && pending.host.equals(invitation.host)
                && Arrays.equals(pending.fingerprint, invitation.fingerprint) ? pending : null;
    }

    static void stage(Context context, StatusClient.Pairing pairing) throws Exception {
        if (!store(context).edit().putString(PENDING, encrypt(pairing)).commit()) throw new java.io.IOException("Não foi possível salvar o pareamento.");
    }

    static void accept(Context context, StatusClient.Pairing pairing) throws Exception {
        SharedPreferences preferences = store(context);
        StatusClient.Pairing current = read(context, ACTIVE);
        if (current != null && pairing.token.equals(current.token) && !preferences.contains(PENDING)) return;
        if (!preferences.edit().putString(ACTIVE, encrypt(pairing)).remove(PENDING).commit())
            throw new java.io.IOException("Não foi possível salvar o pareamento.");
    }

    static StatusClient.Pairing invalidate(Context context, StatusClient.Pairing invalid) {
        SharedPreferences.Editor edit = store(context).edit();
        StatusClient.Pairing pending = read(context, PENDING);
        StatusClient.Pairing active = read(context, ACTIVE);
        if (pending != null && pending.token.equals(invalid.token)) edit.remove(PENDING);
        if (active != null && active.token.equals(invalid.token)) {
            edit.remove(ACTIVE);
            active = null;
        }
        edit.commit();
        return active;
    }

    static void clear(Context context) { store(context).edit().clear().commit(); }
    static void discardPending(Context context) { store(context).edit().remove(PENDING).commit(); }

    static boolean hasHouseAccess(Context context, String slot) {
        return store(context).contains(slot);
    }

    static boolean forgetHouseAccess(Context context, String slot) {
        return store(context).edit().remove(slot).commit();
    }

    static boolean rememberHouseAccess(Context context, String slot, char[] password) {
        byte[] plain = new String(password).getBytes(StandardCharsets.UTF_8);
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, key());
            cipher.updateAAD(slot.getBytes(StandardCharsets.US_ASCII));
            String envelope = encode(cipher.getIV()) + "." + encode(cipher.doFinal(plain));
            return store(context).edit().putString(slot, envelope).commit();
        } catch (Exception unavailable) { return false; }
        finally { Arrays.fill(plain, (byte) 0); }
    }

    static char[] readHouseAccess(Context context, String slot) {
        String value = store(context).getString(slot, null);
        if (value == null) return null;
        byte[] plain = null;
        try {
            String[] parts = value.split("\\.", 2);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, decode(parts[0])));
            cipher.updateAAD(slot.getBytes(StandardCharsets.US_ASCII));
            plain = cipher.doFinal(decode(parts[1]));
            return new String(plain, StandardCharsets.UTF_8).toCharArray();
        } catch (Exception unavailable) { return null; }
        finally { if (plain != null) Arrays.fill(plain, (byte) 0); }
    }

    private static String encrypt(StatusClient.Pairing pairing) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        JSONObject data = new JSONObject().put("host", pairing.host).put("pin", encode(pairing.fingerprint)).put("token", pairing.token);
        byte[] plain = data.toString().getBytes(StandardCharsets.UTF_8);
        try { return encode(cipher.getIV()) + "." + encode(cipher.doFinal(plain)); }
        finally { Arrays.fill(plain, (byte) 0); }
    }

    private static StatusClient.Pairing read(Context context, String slot) {
        String value = store(context).getString(slot, null);
        if (value == null) return null;
        try {
            String[] parts = value.split("\\.", 2);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, decode(parts[0])));
            byte[] plain = cipher.doFinal(decode(parts[1]));
            try {
                JSONObject data = new JSONObject(new String(plain, StandardCharsets.UTF_8));
                return new StatusClient.Pairing(data.getString("host"), decode(data.getString("pin")), data.getString("token"), true);
            } finally { Arrays.fill(plain, (byte) 0); }
        } catch (Exception error) { return null; }
    }

    private static synchronized SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        SecretKey existing = (SecretKey) store.getKey(ALIAS, null);
        if (existing != null) return existing;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return generator.generateKey();
    }

    private static SharedPreferences store(Context context) { return context.getSharedPreferences("secure-pairing", Context.MODE_PRIVATE); }
    private static String encode(byte[] value) { return Base64.encodeToString(value, Base64.NO_WRAP); }
    private static byte[] decode(String value) { return Base64.decode(value, Base64.NO_WRAP); }
}
