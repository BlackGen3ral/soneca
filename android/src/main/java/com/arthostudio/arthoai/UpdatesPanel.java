package com.arthostudio.arthoai;

import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.res.ColorStateList;
import android.net.Uri;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import java.text.DateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.concurrent.CancellationException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

final class UpdatesPanel {
    private final MainActivity owner;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final LinearLayout root;
    private final TextView heading;
    private final TextView message;
    private final TextView timestamp;
    private final Button action;
    private final Button cancel;
    private final Button notes;
    private final ProgressBar progress;
    private final String installedVersion;
    private AndroidUpdater.Release available;
    private AtomicBoolean downloadCancelled;
    private boolean verified;
    private boolean busy;
    private boolean waitingForPermission;

    UpdatesPanel(MainActivity owner) {
        this.owner = owner;
        installedVersion = installedVersion();
        root = owner.column();
        TextView title = owner.label("Atualizações", 31, MainActivity.INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        root.addView(title);
        LinearLayout card = owner.column();
        card.setBackground(owner.surface(MainActivity.SURFACE, 18, MainActivity.BORDER));
        card.setPadding(owner.dp(18), owner.dp(18), owner.dp(18), owner.dp(16));
        root.addView(card, owner.spaced(18));
        TextView installed = owner.label("Versão instalada  " + installedVersion, 12, MainActivity.MUTED, "sans-serif-medium");
        installed.setPadding(owner.dp(9), owner.dp(4), owner.dp(9), owner.dp(4));
        installed.setBackground(owner.surface(0xff1b2b3e, 8, MainActivity.BORDER));
        card.addView(installed, new LinearLayout.LayoutParams(-2, -2));
        heading = owner.label("Verificar nova versão", 23, MainActivity.INK, "sans-serif-medium");
        card.addView(heading, owner.spaced(8));
        message = owner.label("", 15, MainActivity.MUTED, "sans-serif");
        message.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        card.addView(message, owner.spaced(5));
        progress = new ProgressBar(owner, null, android.R.attr.progressBarStyleHorizontal);
        progress.setMax(100);
        progress.setProgressTintList(ColorStateList.valueOf(MainActivity.ACCENT));
        progress.setContentDescription("Progresso da atualização");
        progress.setVisibility(View.GONE);
        card.addView(progress, owner.spaced(12));
        action = owner.button("Verificar atualizações", true);
        action.setOnClickListener(view -> {
            if (busy) return;
            if (available == null) check();
            else if (verified) install();
            else download();
        });
        card.addView(action, owner.spaced(14));
        cancel = owner.button("Cancelar download", false);
        cancel.setVisibility(View.GONE);
        cancel.setOnClickListener(view -> {
            if (downloadCancelled == null) return;
            downloadCancelled.set(true);
            cancel.setEnabled(false);
            message.setText("Cancelando o download…");
        });
        card.addView(cancel, owner.spaced(6));
        notes = owner.button("Ver versão no GitHub", false);
        notes.setVisibility(View.GONE);
        notes.setOnClickListener(view -> {
            if (available == null) return;
            try {
                owner.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(
                        "https://github.com/BlackGen3ral/soneca/releases/tag/android-v" + available.version)));
            } catch (ActivityNotFoundException error) {
                message.setText("Não foi possível abrir a página da versão.");
            }
        });
        card.addView(notes, owner.spaced(6));
        timestamp = owner.label("", 13, MainActivity.MUTED, "sans-serif");
        root.addView(timestamp, owner.spaced(12));
    }

    View view() { return root; }

    void check() {
        if (busy) return;
        setBusy(true);
        heading.setText("Consultando o GitHub");
        message.setText("Verificando a última versão do Android…");
        worker.execute(() -> {
            try {
                AndroidUpdater.Release found = AndroidUpdater.check(installedVersion);
                boolean ready = found != null && AndroidUpdater.isDownloaded(owner.getApplicationContext(), found);
                owner.show(() -> {
                    available = found;
                    verified = ready;
                    owner.indicateUpdate(found != null);
                    timestamp.setText("Última consulta · " + DateFormat.getDateTimeInstance(
                            DateFormat.SHORT, DateFormat.SHORT, new Locale("pt", "BR")).format(new Date()));
                    notes.setVisibility(found == null ? View.GONE : View.VISIBLE);
                    if (found == null) {
                        heading.setText("Você está em dia");
                        message.setText("A versão instalada é a mais recente do canal Android.");
                        action.setText("Verificar atualizações");
                    } else if (ready) {
                        showReady();
                    } else {
                        heading.setText("Versão " + found.version + " disponível");
                        message.setText(String.format(new Locale("pt", "BR"), "%.1f MB para baixar.", found.size / 1048576d));
                        action.setText("Baixar atualização");
                    }
                    setBusy(false);
                });
            } catch (Exception error) {
                owner.show(() -> {
                    heading.setText("Não deu para verificar");
                    message.setText("Confira a conexão com o GitHub e tente novamente.");
                    action.setText("Tentar novamente");
                    setBusy(false);
                });
            }
        });
    }

    private void download() {
        AndroidUpdater.Release release = available;
        AtomicBoolean cancelled = new AtomicBoolean();
        downloadCancelled = cancelled;
        setBusy(true);
        heading.setText("Baixando " + release.version);
        message.setText("Preparando o download…");
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
        cancel.setVisibility(View.VISIBLE);
        cancel.setEnabled(true);
        worker.execute(() -> {
            try {
                AndroidUpdater.download(owner.getApplicationContext(), release, percent -> owner.show(() -> {
                    if (downloadCancelled != cancelled || cancelled.get()) return;
                    progress.setProgress(percent);
                    message.setText(percent == 100 ? "Verificando o arquivo…" : percent + "% baixado");
                }), cancelled::get);
                owner.show(() -> {
                    verified = true;
                    finishDownload();
                    showReady();
                });
            } catch (Exception error) {
                owner.show(() -> {
                    finishDownload();
                    boolean interrupted = error instanceof CancellationException || cancelled.get();
                    heading.setText(interrupted ? "Download interrompido" : "Download não concluído");
                    message.setText("Use Baixar atualização para tentar de novo.");
                });
            }
        });
    }

    private void finishDownload() {
        downloadCancelled = null;
        progress.setVisibility(View.GONE);
        cancel.setVisibility(View.GONE);
        setBusy(false);
    }

    private void showReady() {
        heading.setText("Pronto para instalar");
        message.setText("A versão " + available.version + " foi verificada. Confirme a instalação na tela do Android.");
        action.setText("Instalar atualização");
    }

    private void install() {
        if (!owner.getPackageManager().canRequestPackageInstalls()) {
            try {
                waitingForPermission = true;
                owner.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                        Uri.fromParts("package", owner.getPackageName(), null)));
                message.setText("Permita instalações pelo Artho AI e volte para confirmar.");
            } catch (ActivityNotFoundException error) {
                waitingForPermission = false;
                message.setText("A opção de instalação pelo Artho AI está indisponível neste aparelho.");
            }
            return;
        }
        setBusy(true);
        AndroidUpdater.Release release = available;
        worker.execute(() -> {
            boolean ready = AndroidUpdater.isDownloaded(owner.getApplicationContext(), release);
            owner.show(() -> {
                setBusy(false);
                if (!ready) {
                    verified = false;
                    heading.setText("Baixe a atualização novamente");
                    message.setText("O arquivo baixado mudou ou foi removido.");
                    action.setText("Baixar atualização");
                    return;
                }
                Uri apk = new Uri.Builder().scheme("content").authority(owner.getPackageName() + ".updates")
                        .appendPath(UpdateApkProvider.APK_NAME).build();
                Intent intent = new Intent(Intent.ACTION_VIEW).setDataAndType(apk, UpdateApkProvider.MIME_TYPE)
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                try {
                    owner.startActivity(intent);
                    message.setText("Confirme a atualização na tela do Android.");
                } catch (ActivityNotFoundException | SecurityException error) {
                    message.setText("O Android não abriu o instalador. Confira a permissão e tente de novo.");
                }
            });
        });
    }

    void resumed() {
        if (!waitingForPermission) return;
        waitingForPermission = false;
        if (owner.getPackageManager().canRequestPackageInstalls()) {
            message.setText("Permissão concedida. Toque em Instalar atualização para continuar.");
        }
    }

    void close() {
        if (downloadCancelled != null) downloadCancelled.set(true);
        worker.shutdownNow();
    }

    private void setBusy(boolean active) {
        busy = active;
        action.setEnabled(!active);
    }

    private String installedVersion() {
        try { return owner.getPackageManager().getPackageInfo(owner.getPackageName(), 0).versionName; }
        catch (android.content.pm.PackageManager.NameNotFoundException error) {
            throw new IllegalStateException(error);
        }
    }
}
