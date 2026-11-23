package com.arthostudio.arthoai;

final class RemoteFramePacing {
    enum Quality {
        MOTION("motion", 34), LIGHT("light", 40), LOW("low", 50), DETAIL("detail", 50);

        final String query;
        final int intervalMs;

        Quality(String query, int intervalMs) {
            this.query = query;
            this.intervalMs = intervalMs;
        }
    }

    private Quality quality = Quality.MOTION;
    private int slowFrames, fastFrames;

    Quality quality() { return quality; }

    void received(long timeMs) {
        slowFrames = timeMs > 64 ? slowFrames + 1 : 0;
        fastFrames = timeMs < 35 ? fastFrames + 1 : 0;
        if (slowFrames >= 4) reduce();
        else if (fastFrames >= 45 && quality != Quality.MOTION) {
            quality = quality == Quality.LOW ? Quality.LIGHT : Quality.MOTION;
            fastFrames = 0;
        }
    }

    void reduce() {
        if (quality == Quality.MOTION) quality = Quality.LIGHT;
        else if (quality == Quality.LIGHT) quality = Quality.LOW;
        slowFrames = fastFrames = 0;
    }
}
