package com.arthostudio.arthoai;

final class RemotePointer {
    static final int MAX = 65535;
    private float x = 32768, y = 32768;
    private boolean controlled;
    private boolean visible;

    int x() { return Math.round(x); }
    int y() { return Math.round(y); }
    boolean visible() { return visible; }

    void observe(int hostX, int hostY) {
        if (controlled || hostX < 0 || hostY < 0 || hostX > MAX || hostY > MAX) return;
        x = hostX;
        y = hostY;
        visible = true;
    }

    void set(int nextX, int nextY) {
        x = clamp(nextX);
        y = clamp(nextY);
        controlled = visible = true;
    }

    boolean advance(float dx, float dy, float aspect) {
        int previousX = x(), previousY = y();
        x = clamp(x + dx * MAX / Math.max(1f, aspect));
        y = clamp(y + dy * MAX * Math.min(1f, aspect));
        if (x() == previousX && y() == previousY) return false;
        controlled = visible = true;
        return true;
    }

    int edge(float dx, float dy) {
        int horizontal = dx < 0 && x() == 0 ? 1 : dx > 0 && x() == MAX ? 2 : 0;
        int vertical = dy < 0 && y() == 0 ? 3 : dy > 0 && y() == MAX ? 4 : 0;
        if (horizontal == 0) return vertical;
        if (vertical == 0) return horizontal;
        return Math.abs(dx) >= Math.abs(dy) ? horizontal : vertical;
    }

    private static float clamp(float value) { return Math.max(0f, Math.min(MAX, value)); }
}
