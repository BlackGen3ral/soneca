package com.arthostudio.arthoai;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

public final class WakeScheduleReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        PendingResult pending = goAsync();
        new Thread(() -> {
            try {
                if (WakeSchedules.ACTION.equals(intent.getAction())) {
                    WakeSchedules.fire(context, intent.getLongExtra("when", -1L));
                } else {
                    WakeSchedules.restore(context);
                }
            } finally { pending.finish(); }
        }, "ArthoAI-Wake").start();
    }
}
