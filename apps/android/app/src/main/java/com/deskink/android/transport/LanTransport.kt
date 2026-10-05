package com.deskink.android.transport

import com.deskink.android.protocol.ControlProtocolV1
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.Socket
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

class LanTransport(
    private val host: String,
    comparisonCode: String? = null,
    trustedFingerprint: String? = null,
    private val controlPort: Int = 27185,
) : InputTransport {
    private val gate = Any()
    private val expectedCode = comparisonCode?.trim()?.uppercase().orEmpty()
    private val trustedFingerprint = trustedFingerprint?.trim()?.uppercase()?.takeIf { it.isNotEmpty() }
    private var controlSocket: SSLSocket? = null
    private var inputSocket: DatagramSocket? = null
    private var sessionSecret: ByteArray? = null

    @Volatile override var state = TransportState.DISCONNECTED
        private set
    @Volatile override var metrics = TransportMetrics()
        private set
    @Volatile override var lastError: String? = null
        private set
    @Volatile override var sessionId: Long = 0
        private set
    @Volatile override var monitorCount: Int = 0
        private set
    @Volatile override var selectedMonitorIndex: Int = 0
        private set
    @Volatile var certificateFingerprintHex: String? = null
        private set

    override fun connect() {
        synchronized(gate) {
            disconnectLocked()
            require(host.isNotBlank()) { "Digite o IP do PC" }
            require(
                CODE_PATTERN.matches(expectedCode) ||
                    trustedFingerprint?.matches(FINGERPRINT_PATTERN) == true,
            ) { "Digite o código de pareamento ou use um PC já confiável" }
            state = TransportState.CONNECTING
            lastError = null
            try {
                val rawSocket = Socket().apply {
                    tcpNoDelay = true
                    connect(InetSocketAddress(host, controlPort), CONNECT_TIMEOUT_MS)
                }
                val trust = CapturingTrustManager()
                val context = SSLContext.getInstance("TLS").apply {
                    init(null, arrayOf(trust), SecureRandom())
                }
                val tls = context.socketFactory.createSocket(
                    rawSocket,
                    host,
                    controlPort,
                    true,
                ) as SSLSocket
                tls.soTimeout = HANDSHAKE_TIMEOUT_MS
                tls.enabledProtocols = tls.supportedProtocols.filter {
                    it == "TLSv1.3" || it == "TLSv1.2"
                }.toTypedArray()
                tls.startHandshake()

                val certificateFingerprint = MessageDigest.getInstance("SHA-256")
                    .digest((tls.session.peerCertificates.first() as X509Certificate).encoded)
                val challengeFingerprint = ControlProtocolV1.decodeLanPairChallenge(
                    TcpFraming.read(tls.inputStream, MAXIMUM_CONTROL_FRAME_BYTES),
                )
                check(MessageDigest.isEqual(certificateFingerprint, challengeFingerprint)) {
                    "Fingerprint TLS não corresponde ao desafio do host"
                }
                if (trustedFingerprint != null && expectedCode.isEmpty()) {
                    check(hex(certificateFingerprint) == trustedFingerprint) {
                        "Identidade do PC mudou; confira um novo código"
                    }
                } else {
                    check(comparisonCode(certificateFingerprint) == expectedCode) {
                        "Código de pareamento não confere"
                    }
                }

                TcpFraming.write(
                    tls.outputStream,
                    ControlProtocolV1.encodeLanPairConfirm(certificateFingerprint),
                )
                val ack = ControlProtocolV1.decodeLanPairAck(
                    TcpFraming.read(tls.inputStream, MAXIMUM_CONTROL_FRAME_BYTES),
                )
                val clock = ClockSynchronizer.calibrate(
                    tls.inputStream,
                    tls.outputStream,
                    ack.sessionId,
                )
                val udp = DatagramSocket().apply {
                    connect(InetSocketAddress(host, ack.udpInputPort))
                }
                controlSocket = tls
                inputSocket = udp
                sessionSecret = ack.sessionSecret
                sessionId = ack.sessionId
                monitorCount = ack.monitorCount
                selectedMonitorIndex = ack.selectedMonitorIndex
                certificateFingerprintHex = hex(certificateFingerprint)
                metrics = metrics.copy(
                    controlRttUs = clock.roundTripUs,
                    clockUncertaintyUs = clock.uncertaintyUs,
                )
                state = TransportState.CONNECTED
            } catch (failure: Exception) {
                disconnectLocked()
                lastError = failure.message ?: failure.javaClass.simpleName
                state = TransportState.ERROR
                throw failure
            }
        }
    }

    override fun disconnect() = synchronized(gate) {
        disconnectLocked()
        state = TransportState.DISCONNECTED
    }

    override fun sendInput(frame: ByteArray) = synchronized(gate) {
        val udp = inputSocket.takeIf { state == TransportState.CONNECTED }
            ?: throw IllegalStateException("LAN transport is disconnected")
        val secret = sessionSecret ?: throw IllegalStateException("LAN session secret is unavailable")
        try {
            val authenticated = LanAuthenticatedDatagram.encode(frame, secret)
            udp.send(DatagramPacket(authenticated, authenticated.size))
            metrics = metrics.copy(
                inputFramesSent = metrics.inputFramesSent + 1,
                bytesSent = metrics.bytesSent + authenticated.size,
            )
        } catch (failure: Exception) {
            fail(failure)
        }
    }

    override fun sendControl(frame: ByteArray) = synchronized(gate) {
        val tls = controlSocket.takeIf { state == TransportState.CONNECTED }
            ?: throw IllegalStateException("LAN transport is disconnected")
        try {
            TcpFraming.write(tls.outputStream, frame)
            metrics = metrics.copy(
                controlFramesSent = metrics.controlFramesSent + 1,
                bytesSent = metrics.bytesSent + frame.size,
            )
        } catch (failure: Exception) {
            fail(failure)
        }
    }

    private fun fail(failure: Exception): Nothing {
        disconnectLocked()
        lastError = failure.message ?: failure.javaClass.simpleName
        state = TransportState.ERROR
        throw failure
    }

    private fun disconnectLocked() {
        runCatching { inputSocket?.close() }
        runCatching { controlSocket?.close() }
        sessionSecret?.fill(0)
        inputSocket = null
        controlSocket = null
        sessionSecret = null
        sessionId = 0
        monitorCount = 0
        certificateFingerprintHex = null
    }

    private class CapturingTrustManager : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            require(!chain.isNullOrEmpty()) { "Host TLS não apresentou certificado" }
        }
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    companion object {
        private val CODE_PATTERN = Regex("[0-9A-F]{4}-[0-9A-F]{4}")
        private val FINGERPRINT_PATTERN = Regex("[0-9A-F]{64}")
        private const val CONNECT_TIMEOUT_MS = 5_000
        private const val HANDSHAKE_TIMEOUT_MS = 8_000
        private const val MAXIMUM_CONTROL_FRAME_BYTES = 65_535

        internal fun comparisonCode(fingerprint: ByteArray): String {
            val value = hex(fingerprint).take(8)
            return "${value.substring(0, 4)}-${value.substring(4, 8)}"
        }

        internal fun hex(bytes: ByteArray): String =
            bytes.joinToString("") { "%02X".format(it.toInt() and 0xff) }
    }
}
