plugins {
    id("com.android.application")
}

android {
    namespace = "ru.nexus.board"
    compileSdk = 34

    defaultConfig {
        applicationId = "ru.nexus.board"
        minSdk = 24
        targetSdk = 34
        versionCode = 9
        versionName = "2.0.1"
        buildFeatures { buildConfig = true }
    }

    lint {
        abortOnError = false
        checkReleaseBuilds = false
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation(platform("org.jetbrains.kotlin:kotlin-bom:1.9.24"))
    implementation("androidx.core:core:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.recyclerview:recyclerview:1.3.2")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
}
