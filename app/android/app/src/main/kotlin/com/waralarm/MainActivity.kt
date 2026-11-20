package com.waralarm

import android.Manifest
import android.annotation.SuppressLint
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.core.app.ActivityCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

/** «waralarm/geo» — the Dart side of the background location watcher (GeoWatchService). */
class MainActivity : FlutterActivity() {
    private val reqLocation = 701
    private val reqBackground = 702
    private var pending: MethodChannel.Result? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "waralarm/geo").setMethodCallHandler { call, result ->
            try { handle(call, result) } catch (e: Exception) { result.error("geo", e.message, null) }
        }
    }

    private fun prefs() = GeoWatchService.prefs(this)

    private fun saveConfig(call: MethodCall) {
        val e = prefs().edit()
        call.argument<Double>("radius")?.let { e.putFloat("radius", it.toFloat()) }
        call.argument<Double>("homeLat")?.let { e.putFloat("homeLat", it.toFloat()) }
        call.argument<Double>("homeLon")?.let { e.putFloat("homeLon", it.toFloat()) }
        call.argument<String>("homeName")?.let { e.putString("homeName", it) }
        e.apply()
    }

    private fun status(): Map<String, Any?> {
        val p = prefs()
        val at = p.getLong("fixAt", 0)
        return mapOf(
            "enabled" to p.getBoolean(GeoWatchService.K_ENABLED, false),
            "running" to GeoWatchService.running,
            "location" to GeoWatchService.hasLocation(this),
            "background" to GeoWatchService.hasBackground(this),
            "battery" to batteryIgnored(),
            "lat" to if (at > 0) p.getFloat("fixLat", 0f).toDouble() else null,
            "lon" to if (at > 0) p.getFloat("fixLon", 0f).toDouble() else null,
            "at" to at,
        )
    }

    private fun batteryIgnored(): Boolean = Build.VERSION.SDK_INT < 23 ||
        (getSystemService(POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(packageName)

    private fun handle(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "enable" -> {
                saveConfig(call)
                if (GeoWatchService.hasLocation(this)) { turnOn(); result.success(status()); return }
                if (pending != null) { result.error("busy", "permission request in progress", null); return }
                pending = result
                val perms = mutableListOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
                if (Build.VERSION.SDK_INT >= 33) perms += Manifest.permission.POST_NOTIFICATIONS
                ActivityCompat.requestPermissions(this, perms.toTypedArray(), reqLocation)
            }
            "requestBackground" -> {
                if (GeoWatchService.hasBackground(this) || !GeoWatchService.hasLocation(this)) { result.success(status()); return }
                if (pending != null) { result.error("busy", "permission request in progress", null); return }
                pending = result
                // Android 11+: the system opens the app's location page — pick «Allow all the time».
                ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.ACCESS_BACKGROUND_LOCATION), reqBackground)
            }
            "disable" -> {
                prefs().edit().putBoolean(GeoWatchService.K_ENABLED, false).apply()
                GeoWatchService.stop(this)
                result.success(status())
            }
            "config" -> { saveConfig(call); result.success(null) }
            "status" -> {
                // Turned on but not running (force-stopped, killed) — start again while the app is open.
                if (prefs().getBoolean(GeoWatchService.K_ENABLED, false) && !GeoWatchService.running) GeoWatchService.start(this)
                result.success(status())
            }
            "foreground" -> { GeoWatchService.appForeground = call.arguments == true; result.success(null) }
            "battery" -> { openBatterySettings(); result.success(null) }
            else -> result.notImplemented()
        }
    }

    private fun turnOn() {
        prefs().edit().putBoolean(GeoWatchService.K_ENABLED, true).apply()
        GeoWatchService.start(this)
    }

    @SuppressLint("BatteryLife")
    private fun openBatterySettings() {
        try {
            startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
        } catch (_: Exception) {
            try { startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)) } catch (_: Exception) {}
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != reqLocation && requestCode != reqBackground) return
        val r = pending ?: return
        pending = null
        if (requestCode == reqLocation && GeoWatchService.hasLocation(this)) turnOn()
        try { r.success(status()) } catch (_: Exception) {}
    }
}
