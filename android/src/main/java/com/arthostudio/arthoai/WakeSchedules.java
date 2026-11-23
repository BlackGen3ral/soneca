package com.arthostudio.arthoai;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;

final class WakeSchedules {
    static final String ACTION = "com.arthostudio.arthoai.WAKE_PC";
    private static final String STORE = "pc-wake";

    static synchronized WakeTarget target(Context context) {
        SharedPreferences prefs = prefs(context);
        try {
            return new WakeTarget(prefs.getString("address", null), prefs.getString("mac", null),
                    prefs.getInt("prefix", 0), prefs.getString("name", "Meu PC"));
        } catch (IllegalArgumentException error) { return null; }
    }

    static synchronized void remember(Context context, WakeTarget value) {
        WakeTarget previous = target(context);
        if (previous != null && previous.samePc(value) && previous.name.equals(value.name)) return;
        if (previous != null && !previous.samePc(value)) forget(context);
        prefs(context).edit().putString("address", value.address).putString("mac", value.mac)
                .putInt("prefix", value.prefix).putString("name", value.name).commit();
    }

    static synchronized void forget(Context context) {
        cancelAlarm(context);
        prefs(context).edit().clear().commit();
    }

    static boolean canSetExact(Context context) {
        AlarmManager manager = context.getSystemService(AlarmManager.class);
        return Build.VERSION.SDK_INT < 31 || manager.canScheduleExactAlarms();
    }

    static synchronized boolean save(Context context, String time, int days) {
        if (target(context) == null || !canSetExact(context) || days < 1 || days > 127 || !validTime(time)) return false;
        prefs(context).edit().putString("time", time).putInt("days", days).remove("once").remove("report").commit();
        return installNext(context);
    }

    static synchronized boolean saveOnce(Context context, String time) {
        if (target(context) == null || !canSetExact(context) || !validTime(time)) return false;
        long when = next(ZonedDateTime.now(), time, 127);
        prefs(context).edit().putString("time", time).putInt("days", 127).putLong("once", when).remove("report").commit();
        return installNext(context);
    }

    static synchronized void clearRule(Context context) {
        cancelAlarm(context);
        prefs(context).edit().remove("time").remove("days").remove("report").remove("when").remove("once").commit();
    }

    static synchronized void restore(Context context) { installNext(context); }

    static synchronized void fire(Context context, long intended) {
        SharedPreferences data = prefs(context);
        long now = System.currentTimeMillis();
        if (intended < 0 || data.getLong("when", -1) != intended || target(context) == null) return;
        String result;
        try {
            if (now < intended - 1000 || now - intended > 120000) throw new IllegalStateException("Horário passou.");
            target(context).send(context);
            result = "Último sinal de ligar enviado às " + currentClock();
        } catch (Exception error) {
            result = "Não foi possível enviar o sinal às " + currentClock();
        }
        if (once(context) > 0) clearRule(context);
        data.edit().putString("report", result).commit();
        if (!time(context).isEmpty()) installNext(context);
    }

    static synchronized String time(Context context) { return prefs(context).getString("time", ""); }
    static synchronized int days(Context context) { return prefs(context).getInt("days", 0); }
    static synchronized String report(Context context) { return prefs(context).getString("report", ""); }
    static synchronized long once(Context context) { return prefs(context).getLong("once", 0); }

    private static boolean installNext(Context context) {
        SharedPreferences data = prefs(context);
        String time = time(context);
        int days = days(context);
        if (target(context) == null || !validTime(time) || days < 1 || days > 127 || !canSetExact(context)) return false;
        long next = once(context);
        if (next > 0 && System.currentTimeMillis() - next > 120000) {
            clearRule(context);
            data.edit().putString("report", "O horário único de ligar passou sem envio.").commit();
            return false;
        }
        if (next == 0) next = next(ZonedDateTime.now(), time, days);
        data.edit().putLong("when", next).commit();
        try {
            context.getSystemService(AlarmManager.class).setExactAndAllowWhileIdle(
                    AlarmManager.RTC_WAKEUP, next, request(context, next));
            return true;
        } catch (SecurityException error) { return false; }
    }

    static long next(ZonedDateTime now, String time, int days) {
        LocalTime clock = LocalTime.parse(time);
        ZoneId zone = now.getZone();
        for (int offset = 0; offset <= 14; offset++) {
            LocalDateTime day = now.toLocalDate().plusDays(offset).atTime(clock);
            int bit = day.getDayOfWeek().getValue() % 7;
            if ((days & (1 << bit)) == 0 || zone.getRules().getValidOffsets(day).isEmpty()) continue;
            ZonedDateTime candidate = day.atZone(zone);
            if (candidate.isAfter(now)) return candidate.toInstant().toEpochMilli();
        }
        throw new IllegalArgumentException("Nenhum horário válido.");
    }

    static boolean validTime(String time) { return time.matches("(?:[01][0-9]|2[0-3]):[0-5][0-9]"); }

    private static String currentClock() { return ZonedDateTime.now().format(DateTimeFormatter.ofPattern("HH:mm")); }
    private static SharedPreferences prefs(Context context) { return context.getSharedPreferences(STORE, Context.MODE_PRIVATE); }
    private static void cancelAlarm(Context context) { context.getSystemService(AlarmManager.class).cancel(request(context, -1)); }
    private static PendingIntent request(Context context, long next) {
        Intent signal = new Intent(context, WakeScheduleReceiver.class).setAction(ACTION).putExtra("when", next);
        return PendingIntent.getBroadcast(context, 41, signal, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
}
