import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.waralarm"
    // Pinned to 36: audioplayers/vibration deps require compileSdk >= 34.
    compileSdk = 36
    ndkVersion = flutter.ndkVersion

    compileOptions {
        // Required by flutter_local_notifications (uses java.time backport).
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        // «.app»: a fresh id — the tablet kept a hidden com.waralarm (old PC build, other key)
        // that blocked installing the GitHub APK even after removing the app.
        applicationId = "com.waralarm.app"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // One permanent WarAlarm release key, so every APK installs over the previous one, whether it was
    // built on the PC or on GitHub. PC: android/key.properties (not in git) points at the keystore
    // (%USERPROFILE%\.android\waralarm-release.jks). GitHub Actions (build-android.yml): the same keystore
    // from the ANDROID_KEYSTORE_* secrets, handed over in WARALARM_KEYSTORE*. Neither → the debug key.
    val keyProps = Properties().apply {
        val f = rootProject.file("key.properties"); if (f.exists()) f.inputStream().use { load(it) }
    }
    fun key(prop: String, env: String) = keyProps.getProperty(prop) ?: System.getenv(env)
    val releaseStore = key("storeFile", "WARALARM_KEYSTORE")?.let { file(it) }?.takeIf { it.exists() }
    signingConfigs {
        if (releaseStore != null) {
            create("waralarm") {
                storeFile = releaseStore
                storePassword = key("storePassword", "WARALARM_KEYSTORE_PASSWORD")
                keyAlias = key("keyAlias", "WARALARM_KEY_ALIAS")
                keyPassword = key("keyPassword", "WARALARM_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName(if (releaseStore != null) "waralarm" else "debug")
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
}
