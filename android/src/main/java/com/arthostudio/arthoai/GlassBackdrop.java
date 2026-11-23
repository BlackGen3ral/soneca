package com.arthostudio.arthoai;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.RadialGradient;
import android.graphics.Rect;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;

final class GlassBackdrop extends Drawable {
    private final Paint paint = new Paint();
    private Shader base;
    private Shader glow;

    @Override protected void onBoundsChange(Rect bounds) {
        float width = Math.max(1, bounds.width());
        float height = Math.max(1, bounds.height());
        base = new LinearGradient(bounds.left, bounds.top, bounds.right, bounds.bottom,
                new int[]{0xff191d17, 0xff151915, 0xff131713}, null, Shader.TileMode.CLAMP);
        glow = new RadialGradient(bounds.left + width * .78f, bounds.top + height * .08f, height * .52f,
                new int[]{0x35405230, 0x12303b25, 0x00303b25}, null, Shader.TileMode.CLAMP);
    }

    @Override public void draw(Canvas canvas) {
        paint.setShader(base);
        canvas.drawRect(getBounds(), paint);
        paint.setShader(glow);
        canvas.drawRect(getBounds(), paint);
    }

    @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); invalidateSelf(); }
    @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); invalidateSelf(); }
    @Override public int getOpacity() { return PixelFormat.OPAQUE; }
}
