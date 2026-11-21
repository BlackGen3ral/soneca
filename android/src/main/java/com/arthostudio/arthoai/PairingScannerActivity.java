package com.arthostudio.arthoai;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.ImageFormat;
import android.hardware.Camera;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import com.google.zxing.BinaryBitmap;
import com.google.zxing.PlanarYUVLuminanceSource;
import com.google.zxing.ReaderException;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;
import java.io.IOException;
import java.util.List;

@SuppressWarnings("deprecation")
public final class PairingScannerActivity extends Activity implements SurfaceHolder.Callback {
    static final String EXTRA_CODE = "artho.pairing.code";
    static final String PREFIX = "artho-ai:";
    private static final int CAMERA_PERMISSION = 1;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final QRCodeReader reader = new QRCodeReader();
    private SurfaceView surface;
    private FrameLayout viewfinder;
    private TextView instructions;
    private Camera camera;
    private boolean resumed;

    @Override protected void onCreate(Bundle savedState) {
        super.onCreate(savedState);
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        page.setBackgroundColor(0xff080d15);
        page.setPadding(dp(18), dp(18), dp(18), dp(18));
        page.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(dp(18) + insets.getSystemWindowInsetLeft(), dp(18) + insets.getSystemWindowInsetTop(),
                    dp(18) + insets.getSystemWindowInsetRight(), dp(18) + insets.getSystemWindowInsetBottom());
            return insets;
        });
        instructions = new TextView(this);
        instructions.setText("No PC, abra Artho AI → Celular → Gerar QR Code. Aponte a câmera para o código.");
        instructions.setTextSize(17);
        instructions.setTextColor(Color.WHITE);
        instructions.setPadding(0, 0, 0, dp(12));
        instructions.setAccessibilityLiveRegion(TextView.ACCESSIBILITY_LIVE_REGION_POLITE);
        page.addView(instructions);

        viewfinder = new FrameLayout(this);
        viewfinder.setBackgroundColor(Color.BLACK);
        surface = new SurfaceView(this);
        surface.getHolder().addCallback(this);
        viewfinder.addView(surface, new FrameLayout.LayoutParams(-1, -1, Gravity.CENTER));
        page.addView(viewfinder, new LinearLayout.LayoutParams(-1, 0, 1));
        Button close = new Button(this);
        close.setText("Voltar");
        close.setOnClickListener(view -> finish());
        page.addView(close, new LinearLayout.LayoutParams(-1, -2));
        setContentView(page);

        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.CAMERA}, CAMERA_PERMISSION);
        }
    }

    @Override protected void onResume() {
        super.onResume();
        resumed = true;
        openCamera();
    }

    @Override protected void onPause() {
        resumed = false;
        closeCamera();
        super.onPause();
    }

    @Override public void onRequestPermissionsResult(int code, String[] permissions, int[] grants) {
        super.onRequestPermissionsResult(code, permissions, grants);
        if (code != CAMERA_PERMISSION) return;
        if (grants.length > 0 && grants[0] == PackageManager.PERMISSION_GRANTED) openCamera();
        else instructions.setText("Para escanear, permita a câmera nas configurações do Android.");
    }

    @Override public void surfaceCreated(SurfaceHolder holder) { openCamera(); }
    @Override public void surfaceChanged(SurfaceHolder holder, int format, int width, int height) { }
    @Override public void surfaceDestroyed(SurfaceHolder holder) { closeCamera(); }

    private void openCamera() {
        if (!resumed || camera != null || !surface.getHolder().getSurface().isValid()
                || checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) return;
        try {
            Camera.CameraInfo info = new Camera.CameraInfo();
            int id = 0;
            while (id < Camera.getNumberOfCameras()) {
                Camera.getCameraInfo(id, info);
                if (info.facing == Camera.CameraInfo.CAMERA_FACING_BACK) break;
                id++;
            }
            if (id == Camera.getNumberOfCameras()) throw new IOException("Sem câmera traseira");
            camera = Camera.open(id);
            Camera.Parameters settings = camera.getParameters();
            settings.setPreviewFormat(ImageFormat.NV21);
            List<String> modes = settings.getSupportedFocusModes();
            if (modes != null && modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) {
                settings.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
            }
            camera.setParameters(settings);
            Camera.Size size = camera.getParameters().getPreviewSize();
            int rotation = getWindowManager().getDefaultDisplay().getRotation();
            int degrees = rotation == Surface.ROTATION_90 ? 90 : rotation == Surface.ROTATION_180 ? 180
                    : rotation == Surface.ROTATION_270 ? 270 : 0;
            int orientation = (info.orientation - degrees + 360) % 360;
            camera.setDisplayOrientation(orientation);
            viewfinder.post(() -> {
                boolean upright = orientation == 90 || orientation == 270;
                int width = upright ? size.height : size.width;
                int height = upright ? size.width : size.height;
                float scale = Math.min((float) viewfinder.getWidth() / width, (float) viewfinder.getHeight() / height);
                if (scale > 0) surface.setLayoutParams(new FrameLayout.LayoutParams(
                        Math.round(width * scale), Math.round(height * scale), Gravity.CENTER));
            });
            camera.setPreviewDisplay(surface.getHolder());
            camera.startPreview();
            scanNextFrame();
        } catch (IOException | RuntimeException error) {
            closeCamera();
            instructions.setText("A câmera traseira está indisponível. Volte e tente novamente.");
        }
    }

    private void scanNextFrame() {
        if (camera == null) return;
        camera.setOneShotPreviewCallback((pixels, source) -> {
            if (camera != source || pixels == null) {
                if (camera == source) handler.postDelayed(this::scanNextFrame, 180);
                return;
            }
            Camera.Size size = source.getParameters().getPreviewSize();
            try {
                PlanarYUVLuminanceSource frame = new PlanarYUVLuminanceSource(
                        pixels, size.width, size.height, 0, 0, size.width, size.height, false);
                String text = reader.decode(new BinaryBitmap(new HybridBinarizer(frame))).getText();
                if (text.startsWith(PREFIX)) {
                    String code = text.substring(PREFIX.length());
                    try {
                        StatusClient.Pairing.parse(code);
                        setResult(RESULT_OK, new Intent().putExtra(EXTRA_CODE, code));
                        closeCamera();
                        finish();
                        return;
                    } catch (IllegalArgumentException ignored) { }
                }
                instructions.setText("Use o QR Code da aba Celular do Artho AI no PC.");
            } catch (ReaderException ignored) {
                // Continue scanning until a QR code is readable.
            }
            handler.postDelayed(this::scanNextFrame, 180);
        });
    }

    private void closeCamera() {
        handler.removeCallbacksAndMessages(null);
        if (camera == null) return;
        camera.setPreviewCallback(null);
        camera.release();
        camera = null;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
