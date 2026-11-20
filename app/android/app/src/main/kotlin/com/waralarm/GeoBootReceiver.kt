package com.waralarm

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat

/**
 * After a reboot or an app update the geo watcher comes back by itself — if the user had it on.
 * Android lets a location service start from the background only with «Allow all the time»;
 * without it we ask with one tap instead of failing silently.
 */
class GeoBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action !in setOf(Intent.ACTION_BOOT_COMPLETED, Intent.ACTION_MY_PACKAGE_REPLACED, "android.intent.action.QUICKBOOT_POWERON")) return
        if (!GeoWatchService.prefs(context).getBoolean(GeoWatchService.K_ENABLED, false)) return
        if (GeoWatchService.hasLocation(context) && GeoWatchService.hasBackground(context) && GeoWatchService.start(context)) return
        askToResume(context)
    }

    private fun askToResume(c: Context) {
        val nm = c.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26)
            nm.createNotificationChannel(NotificationChannel("geo_resume", "Відновити стеження", NotificationManager.IMPORTANCE_DEFAULT))
        val n = NotificationCompat.Builder(c, "geo_resume")
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("🛰 Стеження за геопозицією на паузі")
            .setContentText("Натисни, щоб WarAlarm знову стежив за ціллями біля тебе.")
            .setAutoCancel(true)
            .setContentIntent(GeoWatchService.openAppIntent(c, 5))
            .build()
        try { nm.notify(GeoWatchService.ID_RESUME, n) } catch (_: Exception) {}
    }
}
