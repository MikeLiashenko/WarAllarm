package com.waralarm

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * 🛰 Background location watcher, pure Kotlin — no Flutter engine and no third-party
 * background plugin (flutter_background_service crashed at launch on Galaxy Tab S10+).
 * It starts only when the user turns it on, keeps running with the app closed / phone
 * locked (foreground service, type «location»), comes back after a reboot, and itself:
 *   • follows the phone's location (GPS + network),
 *   • every 30 s reads the live targets from the WarAlarm Firebase (same data as the app),
 *   • raises a loud alarm notification when a target is within the user's radius of
 *     WHERE THE PHONE IS NOW, and an all-clear after a quiet minute (the bot's rule).
 * While the app is open it stays quiet: the in-app siren watches «📍 Я зараз» itself.
 */
class GeoWatchService : Service(), LocationListener {

    companion object {
        const val PREFS = "waralarm_geo"
        const val K_ENABLED = "enabled"
        private const val CH_WATCH = "geo_watch"
        private const val CH_ALARM = "geo_alarm_v1"
        private const val CH_CLEAR = "geo_clear"
        private const val ID_WATCH = 4101
        private const val ID_ALARM = 4102
        private const val ID_CLEAR = 4103
        const val ID_RESUME = 4104
        private const val DB = "https://waralarm-c8c1f-default-rtdb.europe-west1.firebasedatabase.app"
        /** A fix older than this is not «where you are» any more — fall back to the home point. */
        private const val FIX_FRESH_MS = 30 * 60_000L
        /** Server silent longer than this — no alarms on stale data. */
        private const val STATE_STALE_MS = 10 * 60_000L

        @Volatile var running = false
        @Volatile var appForeground = false

        fun prefs(c: Context): SharedPreferences = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

        fun hasLocation(c: Context) =
            ContextCompat.checkSelfPermission(c, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
                ContextCompat.checkSelfPermission(c, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED

        fun hasBackground(c: Context) = Build.VERSION.SDK_INT < 29 ||
            ContextCompat.checkSelfPermission(c, Manifest.permission.ACCESS_BACKGROUND_LOCATION) == PackageManager.PERMISSION_GRANTED

        fun start(c: Context): Boolean {
            if (!hasLocation(c)) return false
            return try {
                ContextCompat.startForegroundService(c, Intent(c, GeoWatchService::class.java)); true
            } catch (e: Exception) { false } // e.g. not allowed to start from the background right now
        }

        fun stop(c: Context) {
            try { c.stopService(Intent(c, GeoWatchService::class.java)) } catch (_: Exception) {}
            val nm = c.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            nm.cancel(ID_ALARM); nm.cancel(ID_RESUME)
        }

        fun km(aLat: Double, aLon: Double, bLat: Double, bLon: Double): Double {
            val r = 6371.0; val rad = Math.PI / 180
            val dLa = (bLat - aLat) * rad; val dLo = (bLon - aLon) * rad
            val h = sin(dLa / 2) * sin(dLa / 2) + cos(aLat * rad) * cos(bLat * rad) * sin(dLo / 2) * sin(dLo / 2)
            return r * 2 * atan2(sqrt(h), sqrt(1 - h))
        }

        fun openAppIntent(c: Context, code: Int): PendingIntent {
            val i = (c.packageManager.getLaunchIntentForPackage(c.packageName) ?: Intent(c, MainActivity::class.java))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
            return PendingIntent.getActivity(c, code, i, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        }
    }

    private var exec: ScheduledExecutorService? = null
    // Без нього вночі (Doze) процесор спить і перевірка раз на 30 с не спрацьовує — тривога запізниться.
    private var wake: PowerManager.WakeLock? = null
    private var locationOn = false
    @Volatile private var alarmOn = false
    @Volatile private var calmSince = 0L

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannels()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (!hasLocation(this)) { stopSelf(); return START_NOT_STICKY }
        try {
            ServiceCompat.startForeground(this, ID_WATCH, watchNotification("Шукаю геопозицію…"),
                if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION else 0)
        } catch (e: Exception) {
            // Android refused (no permission / started from the background) — never crash the app over it.
            stopSelf(); return START_NOT_STICKY
        }
        running = true
        if (wake == null) wake = (getSystemService(POWER_SERVICE) as PowerManager)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "WarAlarm:geoWatch").apply { setReferenceCounted(false); acquire() }
        startLocation()
        if (exec == null) {
            exec = Executors.newSingleThreadScheduledExecutor().also {
                it.scheduleWithFixedDelay({ try { tick() } catch (_: Throwable) {} }, 3, 30, TimeUnit.SECONDS)
            }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        try { (getSystemService(LOCATION_SERVICE) as LocationManager).removeUpdates(this) } catch (_: Exception) {}
        locationOn = false
        exec?.shutdownNow(); exec = null
        try { wake?.release() } catch (_: Exception) {}
        wake = null
        super.onDestroy()
    }

    // ── Location ────────────────────────────────────────────────────────────
    private fun startLocation() {
        if (locationOn) return
        val lm = getSystemService(LOCATION_SERVICE) as LocationManager
        // Network: cheap, every minute / 100 m. GPS: every 3 min / 200 m — precise but sparing the battery.
        for ((provider, every, meters) in listOf(
            Triple(LocationManager.NETWORK_PROVIDER, 60_000L, 100f),
            Triple(LocationManager.GPS_PROVIDER, 180_000L, 200f),
        )) {
            try {
                if (lm.allProviders.contains(provider)) {
                    lm.requestLocationUpdates(provider, every, meters, this, Looper.getMainLooper())
                    locationOn = true
                }
            } catch (_: SecurityException) {} catch (_: Exception) {}
        }
        try {
            listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER)
                .mapNotNull { p -> try { lm.getLastKnownLocation(p) } catch (_: Exception) { null } }
                .maxByOrNull { it.time }?.let { onLocationChanged(it) }
        } catch (_: Exception) {}
    }

    override fun onLocationChanged(loc: Location) {
        val p = prefs(this)
        val prevAt = p.getLong("fixAt", 0)
        val prevAcc = p.getFloat("fixAcc", 1e6f)
        // A rough network fix doesn't replace a fresh, sharper GPS one.
        if (System.currentTimeMillis() - prevAt < 180_000 && loc.accuracy > prevAcc * 3 && loc.accuracy > 500) return
        p.edit().putFloat("fixLat", loc.latitude.toFloat()).putFloat("fixLon", loc.longitude.toFloat())
            .putFloat("fixAcc", loc.accuracy).putLong("fixAt", System.currentTimeMillis()).apply()
    }

    // Old Androids (< 10) call these abstract methods — keep them, or a provider change crashes the service.
    @Deprecated("Deprecated in Java")
    override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
    override fun onProviderEnabled(provider: String) {}
    override fun onProviderDisabled(provider: String) {}

    // ── Every 30 s: targets near the phone → alarm / all-clear ──────────────
    private fun tick() {
        val p = prefs(this)
        if (!p.getBoolean(K_ENABLED, false)) { stopSelf(); return }
        val now = System.currentTimeMillis()
        val fresh = now - p.getLong("fixAt", 0) < FIX_FRESH_MS
        val lat: Double; val lon: Double; val where: String
        if (fresh) {
            lat = p.getFloat("fixLat", 0f).toDouble(); lon = p.getFloat("fixLon", 0f).toDouble(); where = "де ти зараз"
        } else if (p.contains("homeLat")) {
            lat = p.getFloat("homeLat", 0f).toDouble(); lon = p.getFloat("homeLon", 0f).toDouble()
            where = "геопозиції немає — стежу за «${p.getString("homeName", "дім")}»"
        } else { updateWatch("Чекаю на геопозицію… Увімкни «Місцезнаходження» в телефоні."); return }
        val radius = p.getFloat("radius", 25f).toDouble()

        val ts = getJson("$DB/state/ts.json")?.toLongOrNull()
        if (ts == null || now - ts > STATE_STALE_MS) { updateWatch("📡 Немає свіжих даних з сервера · $where"); return }
        val targets = liveTargets() ?: run { updateWatch("📡 Немає зв'язку з сервером · $where"); return }

        var nearest = Double.MAX_VALUE
        var top: Target? = null
        for (t in targets) {
            val d = km(lat, lon, t.lat, t.lon)
            if (d < nearest) { nearest = d; top = t }
        }
        val alarm = nearest <= radius
        val what = top?.let { t ->
            "${t.label}${if (t.n > 1) " ×${t.n}" else ""} ~${nearest.roundToInt()} км" + (if (t.place.isNotEmpty()) " · ${t.place}" else "")
        } ?: ""

        if (alarm) {
            calmSince = 0
            if (!alarmOn) { alarmOn = true; if (!appForeground) notifyAlarm(what, radius) }
        } else if (alarmOn) {
            if (calmSince == 0L) calmSince = now
            else if (now - calmSince >= 60_000) { // ціль на межі зони «блимає» — відбій після хвилини тиші
                alarmOn = false; calmSince = 0
                val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
                nm.cancel(ID_ALARM)
                if (!appForeground) notifyClear()
            }
        }
        updateWatch(
            (if (alarm) "🔴 Ціль у зоні: $what" else if (top != null) "Найближча: $what" else "Поруч чисто") +
                " · зона ${radius.roundToInt()} км · $where"
        )
    }

    private class Target(val lat: Double, val lon: Double, val label: String, val n: Int, val place: String)

    /// Живі точні цілі. Спершу компактний «state/near» (~60 байт на ціль); старий сервер без нього —
    /// повний «state/threats». null — немає зв'язку.
    private fun liveTargets(): List<Target>? {
        val near = getJson("$DB/state/near.json") ?: return null
        val out = mutableListOf<Target>()
        if (near.trim() != "null") {
            val arr = try { JSONObject(near).optJSONArray("t") } catch (_: Exception) { null } ?: return out
            for (i in 0 until arr.length()) {
                val a = arr.optJSONArray(i) ?: continue
                out += Target(a.optDouble(0), a.optDouble(1), a.optString(2, "Ціль"), a.optInt(3, 1), a.optString(4, ""))
            }
            return out
        }
        val arr = try { JSONArray(getJson("$DB/state/threats.json")?.takeIf { it.trim() != "null" } ?: "[]") } catch (_: Exception) { return null }
        for (i in 0 until arr.length()) {
            val t = arr.optJSONObject(i) ?: continue
            // Як у сервера: збиті, стартові кола, прогнози, «по області» та ті, що пролетіли, — не тривога.
            if (t.optBoolean("down") || t.optBoolean("launch") || t.optBoolean("forecast") || t.optBoolean("approx")) continue
            if (t.optString("stage") == "Passed" || !t.has("lat") || !t.has("lon")) continue
            out += Target(t.getDouble("lat"), t.getDouble("lon"), t.optString("label", "Ціль"), t.optInt("n", 1), t.optString("place"))
        }
        return out
    }

    private fun getJson(url: String): String? = try {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 15_000; c.readTimeout = 20_000
        c.setRequestProperty("Accept", "application/json")
        try { if (c.responseCode == 200) c.inputStream.bufferedReader().use { it.readText() } else null } finally { c.disconnect() }
    } catch (_: Exception) { null }

    // ── Notifications ───────────────────────────────────────────────────────
    private fun createChannels() {
        if (Build.VERSION.SDK_INT < 26) return
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        nm.createNotificationChannel(NotificationChannel(CH_WATCH, "Стеження за геопозицією", NotificationManager.IMPORTANCE_LOW).apply {
            description = "Постійне сповіщення: WarAlarm стежить за ціллями біля тебе"
            setShowBadge(false)
        })
        nm.createNotificationChannel(NotificationChannel(CH_ALARM, "Тривога поруч", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "Ціль у твоїй зоні — гучно, навіть уночі"
            setSound(RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM),
                AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
            enableVibration(true)
            vibrationPattern = longArrayOf(0, 800, 400, 800, 400, 800)
            lockscreenVisibility = Notification.VISIBILITY_PUBLIC
        })
        nm.createNotificationChannel(NotificationChannel(CH_CLEAR, "Відбій", NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = "Ціль вилетіла з твоєї зони"
        })
    }

    private fun watchNotification(text: String): Notification =
        NotificationCompat.Builder(this, CH_WATCH)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("🛰 WarAlarm стежить за тобою")
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setContentIntent(openAppIntent(this, 1))
            .build()

    private fun updateWatch(text: String) {
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        try { nm.notify(ID_WATCH, watchNotification(text)) } catch (_: Exception) {}
    }

    private fun notifyAlarm(what: String, radius: Double) {
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        val text = (if (what.isNotEmpty()) "🎯 $what\n" else "") + "Ціль ближче ${radius.roundToInt()} км від тебе. В укриття! Подалі від вікон."
        val n = NotificationCompat.Builder(this, CH_ALARM)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("🔴 ТРИВОГА поруч")
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setSound(RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM))
            .setVibrate(longArrayOf(0, 800, 400, 800, 400, 800))
            .setAutoCancel(true)
            .setContentIntent(openAppIntent(this, 2))
            .setFullScreenIntent(openAppIntent(this, 3), true)
            .build()
        n.flags = n.flags or Notification.FLAG_INSISTENT // звучить, доки не торкнешся — щоб розбудило
        try { nm.notify(ID_ALARM, n) } catch (_: Exception) {}
    }

    private fun notifyClear() {
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        val n = NotificationCompat.Builder(this, CH_CLEAR)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("🟢 Відбій — поруч чисто")
            .setContentText("Ціль вилетіла з твоєї зони. До офіційного відбою — уважно.")
            .setAutoCancel(true)
            .setContentIntent(openAppIntent(this, 4))
            .build()
        try { nm.notify(ID_CLEAR, n) } catch (_: Exception) {}
    }
}
