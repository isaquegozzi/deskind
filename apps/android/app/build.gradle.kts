plugins {
    id("com.android.application")
}

android {
    namespace = "com.deskink.android"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.deskink.android"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // MVP sideload build. Replace with a private release key before store distribution.
            signingConfig = signingConfigs.getByName("debug")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests.isReturnDefaultValues = true
    }

    sourceSets.getByName("test").resources.directories.add(
        rootProject.layout.projectDirectory.dir("../../protocol/test-vectors").asFile.absolutePath,
    )
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}
