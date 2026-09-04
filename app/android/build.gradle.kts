allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
}
// Force every Flutter plugin module to compile against a modern SDK. Some
// plugins (e.g. vibration) still declare compileSdk 33, but their androidx
// dependencies now require 34+, which breaks the build. This lifts them all.
// Registered BEFORE evaluationDependsOn so no subproject is evaluated yet.
subprojects {
    afterEvaluate {
        val ext = extensions.findByName("android")
        if (ext is com.android.build.gradle.BaseExtension) {
            val current = ext.compileSdkVersion
                ?.substringAfter("android-")?.toIntOrNull() ?: 0
            if (current < 36) ext.compileSdkVersion(36)
        }
    }
}

subprojects {
    project.evaluationDependsOn(":app")
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
