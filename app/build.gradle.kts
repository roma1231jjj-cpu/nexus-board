import java.util.Properties

plugins {
    id("com.android.application")
}

val keystorePropsFile = rootProject.file("keystore.properties")
val keystoreProps = Properties().apply {
    if (keystorePropsFile.exists()) keystorePropsFile.inputStream().use { load(it) }
}

fun signValue(prop: String, env: String): String? {
    val v = keystoreProps.getProperty(prop)?.takeIf { it.isNotBlank() }
        ?: System.getenv(env)?.takeIf { it.isNotBlank() }
    return v
}

val releaseStore = signValue("storeFile", "NEXUS_BOARD_KEYSTORE")
val releaseStorePassword = signValue("storePassword", "NEXUS_BOARD_KEYSTORE_PASSWORD")
val releaseAlias = signValue("keyAlias", "NEXUS_BOARD_ALIAS")
val releaseKeyPassword = signValue("keyPassword", "NEXUS_BOARD_KEY_PASSWORD")
val releaseKeyMissing = releaseStore == null || releaseStorePassword == null
        || releaseAlias == null || releaseKeyPassword == null

android {
    namespace = "ru.nexus.board"
    compileSdk = 34

    defaultConfig {
        applicationId = "ru.nexus.board"
        minSdk = 24
        targetSdk = 34
        versionCode = 18
        versionName = "2.2.4"
        buildFeatures { buildConfig = true }
    }

    testOptions {
        unitTests.isIncludeAndroidResources = true
    }

    lint {
        abortOnError = false
        checkReleaseBuilds = false
    }

    signingConfigs {
        create("release") {
            if (!releaseKeyMissing) {
                storeFile = file(releaseStore!!)
                storePassword = releaseStorePassword
                keyAlias = releaseAlias
                keyPassword = releaseKeyPassword
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (releaseKeyMissing) null else signingConfigs.getByName("release")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

tasks.matching { it.name.startsWith("assembleRelease") || it.name.startsWith("bundleRelease") }
    .configureEach {
        doFirst {
            if (releaseKeyMissing) {
                throw GradleException(
                    "Release keystore is not configured. Create keystore.properties in the project root:\n" +
                        "  storeFile=/absolute/path/nexus-board.jks\n" +
                        "  storePassword=...\n" +
                        "  keyAlias=...\n" +
                        "  keyPassword=...\n" +
                        "or set NEXUS_BOARD_KEYSTORE, NEXUS_BOARD_KEYSTORE_PASSWORD, " +
                        "NEXUS_BOARD_ALIAS, NEXUS_BOARD_KEY_PASSWORD."
                )
            }
        }
    }

dependencies {
    implementation(platform("org.jetbrains.kotlin:kotlin-bom:1.9.24"))
    implementation("androidx.core:core:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.recyclerview:recyclerview:1.3.2")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
}