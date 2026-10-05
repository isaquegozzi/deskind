package com.deskink.android.transport

import com.deskink.android.protocol.ControlProtocolV1
import java.net.InetSocketAddress
import java.net.Socket
import java.security.SecureRandom
import java.io.BufferedOutputStream
import java.io.OutputStream

class AdbUsbTransport(
    private val host: String = "127.0.0.1",
    private val controlPort: Int = 27183,
    private val inputPort: Int = 27184,
) : InputTransport {
    private val gate = Any()
    private val random = SecureRandom()
    private var controlSocket: Socket? = null
    private var inputSocket: Socket? = null
    private var controlOutput: OutputStream? = null
    private var inputOutput: OutputStream? = null

    @Volatile
    override var state: TransportState = TransportState.DISCONNECTED
        private set

    @Volatile
    override var metrics: TransportMetrics = TransportMetrics()
        private set

    @Volatile
    override var lastError: String? = null
        private set

    @Volatile
    override var sessionId: Long = 0
        private set

    @Volatile
    override var monitorCount: Int = 0
        private set

    @Volatile
    override var selectedMonitorIndex: Int = 0
        private set

    override fun connect() {
        synchronized(gate) {
            closeSockets()
            sessionId = 0
            monitorCount = 0
            state = TransportState.CONNECTING
            lastError = null
            try {
            val newControl = openSocket(controlPort)
            val newControlOutput = BufferedOutputStream(newControl.getOutputStream(), SOCKET_BUFFER_BYTES)
            TcpFraming.write(newControlOutput, ControlProtocolV1.encodeHello(random.nextLong()))
            val helloAck = ControlProtocolV1.decodeHelloAck(
                TcpFraming.read(newControl.getInputStream(), MAXIMUM_CONTROL_FRAME_BYTES),
            )
            val clock = ClockSynchronizer.calibrate(
                newControl.getInputStream(),
                newControlOutput,
                helloAck.sessionId,
            )

            val newInput = openSocket(inputPort)
            val newInputOutput = BufferedOutputStream(newInput.getOutputStream(), SOCKET_BUFFER_BYTES)
            TcpFraming.write(
                newInputOutput,
                ControlProtocolV1.encodeInputBind(helloAck.sessionId, helloAck.inputBindToken),
            )

                controlSocket = newControl
                inputSocket = newInput
                controlOutput = newControlOutput
                inputOutput = newInputOutput
                sessionId = helloAck.sessionId
                monitorCount = helloAck.monitorCount
                selectedMonitorIndex = helloAck.selectedMonitorIndex
                metrics = metrics.copy(
                    controlRttUs = clock.roundTripUs,
                    clockUncertaintyUs = clock.uncertaintyUs,
                )
                state = TransportState.CONNECTED
            } catch (failure: Exception) {
                closeSockets()
                lastError = failure.message ?: failure.javaClass.simpleName
                state = TransportState.ERROR
                throw failure
            }
        }
    }

    override fun disconnect() {
        synchronized(gate) {
            closeSockets()
            sessionId = 0
            monitorCount = 0
            state = TransportState.DISCONNECTED
        }
    }

    override fun sendInput(frame: ByteArray) {
        synchronized(gate) {
            inputSocket.takeIf { state == TransportState.CONNECTED }
                ?: throw IllegalStateException("USB transport is disconnected")
            val output = inputOutput ?: throw IllegalStateException("USB input stream is unavailable")
            try {
                TcpFraming.write(output, frame)
                metrics = metrics.copy(
                    inputFramesSent = metrics.inputFramesSent + 1,
                    bytesSent = metrics.bytesSent + frame.size,
                )
            } catch (failure: Exception) {
                lastError = failure.message ?: failure.javaClass.simpleName
                state = TransportState.ERROR
                closeSockets()
                throw failure
            }
        }
    }

    override fun sendControl(frame: ByteArray) {
        synchronized(gate) {
            controlSocket.takeIf { state == TransportState.CONNECTED }
                ?: throw IllegalStateException("USB transport is disconnected")
            val output = controlOutput ?: throw IllegalStateException("USB control stream is unavailable")
            try {
                TcpFraming.write(output, frame)
                metrics = metrics.copy(
                    controlFramesSent = metrics.controlFramesSent + 1,
                    bytesSent = metrics.bytesSent + frame.size,
                )
            } catch (failure: Exception) {
                lastError = failure.message ?: failure.javaClass.simpleName
                state = TransportState.ERROR
                closeSockets()
                throw failure
            }
        }
    }

    private fun openSocket(port: Int): Socket = Socket().apply {
        tcpNoDelay = true
        keepAlive = true
        connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
        soTimeout = HANDSHAKE_TIMEOUT_MS
    }

    private fun closeSockets() {
        runCatching { inputSocket?.close() }
        runCatching { controlSocket?.close() }
        inputSocket = null
        controlSocket = null
        inputOutput = null
        controlOutput = null
    }

    companion object {
        private const val CONNECT_TIMEOUT_MS = 3_000
        private const val HANDSHAKE_TIMEOUT_MS = 5_000
        private const val MAXIMUM_CONTROL_FRAME_BYTES = 65_535
        private const val SOCKET_BUFFER_BYTES = 8_192
    }
}
