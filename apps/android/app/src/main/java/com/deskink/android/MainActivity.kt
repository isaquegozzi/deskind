package com.deskink.android

import android.app.Activity
import android.app.AlertDialog
import android.content.res.ColorStateList
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import com.deskink.android.input.AxisCapability
import com.deskink.android.input.StylusInputEngine
import com.deskink.android.input.StylusPacketPublisher
import com.deskink.android.input.StylusTelemetrySnapshot
import com.deskink.android.protocol.ControlProtocolV1
import com.deskink.android.transport.AdbUsbTransport
import com.deskink.android.transport.InputTransport
import com.deskink.android.transport.DiscoveredDeskInkHost
import com.deskink.android.transport.LanDiscoveryClient
import com.deskink.android.transport.LanTransport
import com.deskink.android.transport.TransportState
import com.deskink.android.ui.StylusCaptureView
import java.util.Locale
import java.util.concurrent.Executors

class MainActivity : Activity() {
    private val inputEngine = StylusInputEngine()
    private val usbTransport = AdbUsbTransport()
    @Volatile private var transport: InputTransport = usbTransport
    private val packetPublisher = StylusPacketPublisher { transport }
    private val connectionExecutor = Executors.newSingleThreadExecutor()
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var telemetryText: TextView
    private lateinit var capabilityText: TextView
    private lateinit var transportText: TextView
    private lateinit var connectButton: Button
    private lateinit var lanButton: Button
    private lateinit var discoveryButton: Button
    private lateinit var lanHostInput: EditText
    private lateinit var lanCodeInput: EditText
    private lateinit var penModeButton: Button
    private lateinit var monitorButton: Button
    private lateinit var outputModeButton: Button
    private lateinit var overlayToolButton: Button
    private lateinit var overlayInteractionButton: Button
    private lateinit var twoFingerGestureButton: Button
    private lateinit var inputDispatchButton: Button
    private lateinit var connectionPanel: LinearLayout
    private lateinit var shortcutPanel: LinearLayout
    private lateinit var settingsPanel: LinearLayout
    private lateinit var diagnosticsPanel: LinearLayout
    private lateinit var performanceText: TextView
    private val mainModeButtons = mutableMapOf<Int, Button>()
    private var penScrollEnabled = false
    private var monitorCount = 0
    private var selectedMonitorIndex = 0
    private var outputMode = 0
    private var overlayTool = 0
    private var overlayInteractive = false
    private var twoFingerGesture = 1
    private var unbufferedDispatchEnabled = true
    private var selectedDiscoveryFingerprint: String? = null

    private val refreshTelemetry = object : Runnable {
        override fun run() {
            render(inputEngine.snapshot())
            handler.postDelayed(this, TELEMETRY_REFRESH_MS)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        penScrollEnabled = getPreferences(MODE_PRIVATE).getBoolean(PEN_SCROLL_PREFERENCE, false)
        outputMode = getPreferences(MODE_PRIVATE).getInt(OUTPUT_MODE_PREFERENCE, 0).coerceIn(0, 2)
        overlayTool = getPreferences(MODE_PRIVATE).getInt(OVERLAY_TOOL_PREFERENCE, 0).coerceIn(0, 2)
        twoFingerGesture = getPreferences(MODE_PRIVATE)
            .getInt(TWO_FINGER_GESTURE_PREFERENCE, 1)
            .coerceIn(0, 2)
        unbufferedDispatchEnabled = getPreferences(MODE_PRIVATE)
            .getBoolean(UNBUFFERED_DISPATCH_PREFERENCE, true)
        setContentView(buildContent())
    }

    override fun onResume() {
        super.onResume()
        handler.post(refreshTelemetry)
    }

    override fun onPause() {
        handler.removeCallbacks(refreshTelemetry)
        super.onPause()
    }

    override fun onDestroy() {
        packetPublisher.close()
        transport.disconnect()
        connectionExecutor.shutdownNow()
        super.onDestroy()
    }

    private fun buildContent(): View = buildEdgeContent()

    private fun buildEdgeContent(): View {
        val density = resources.displayMetrics.density
        val outerPadding = (12 * density).toInt()
        val gap = (8 * density).toInt()
        val panelTop = (64 * density).toInt()
        val panelWidth = minOf(
            (460 * density).toInt(),
            resources.displayMetrics.widthPixels - (outerPadding * 2),
        )

        val root = FrameLayout(this).apply {
            setPadding(outerPadding, outerPadding, outerPadding, outerPadding)
            setBackgroundColor(getColor(R.color.deskink_background))
            setOnApplyWindowInsetsListener { view, insets ->
                @Suppress("DEPRECATION")
                view.setPadding(
                    outerPadding + insets.systemWindowInsetLeft,
                    outerPadding + insets.systemWindowInsetTop,
                    outerPadding + insets.systemWindowInsetRight,
                    outerPadding + insets.systemWindowInsetBottom,
                )
                insets
            }
        }

        root.addView(
            StylusCaptureView(
                context = this,
                inputEngine = inputEngine,
                eventObserver = { event, width, height ->
                    packetPublisher.updateCapabilities(inputEngine.currentCapabilities())
                    packetPublisher.publish(event, width, height)
                },
                twoFingerTapObserver = { performTwoFingerGesture() },
                unbufferedDispatchEnabled = { unbufferedDispatchEnabled },
            ),
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT,
            ),
        )

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(gap, 0, gap, 0)
            addView(TextView(context).apply {
                text = getString(R.string.telemetry_title)
                textSize = 22f
                setTextColor(getColor(R.color.deskink_text))
                setTypeface(typeface, Typeface.BOLD)
            })
            transportText = TextView(context).apply {
                text = "USB • desconectado"
                textSize = 12f
                setTextColor(getColor(R.color.deskink_muted))
                setOnClickListener { toggleExclusivePanel(diagnosticsPanel) }
            }
            addView(transportText)
        }
        root.addView(header, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP or Gravity.START,
        ))

        root.addView(
            compactButton("Conexão") { toggleExclusivePanel(connectionPanel) },
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.END,
            ),
        )

        val modeRail = sectionPanel().apply {
            listOf("Cursor", "Scroll", "Caneta", "Overlay", "Marca", "Apagar")
                .forEachIndexed { mode, label ->
                    val button = compactButton(label) { selectMainMode(mode) }
                    mainModeButtons[mode] = button
                    addView(button, LinearLayout.LayoutParams(
                        (104 * density).toInt(),
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                    ))
                }
        }
        root.addView(modeRail, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.START or Gravity.CENTER_VERTICAL,
        ))

        val overlayActions = sectionPanel().apply {
            orientation = LinearLayout.HORIZONTAL
            addView(overlayCommandButton("Desfazer", 0))
            addView(overlayCommandButton("Refazer", 1))
            addView(overlayCommandButton("Limpar", 2))
        }
        root.addView(overlayActions, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.START or Gravity.BOTTOM,
        ))

        val utilities = sectionPanel().apply {
            orientation = LinearLayout.HORIZONTAL
            monitorButton = compactButton("Monitor") { selectNextMonitor() }
            addView(monitorButton)
            addView(compactButton("Atalhos") { toggleExclusivePanel(shortcutPanel) })
            addView(compactButton("Ajustes") { toggleExclusivePanel(settingsPanel) })
        }
        root.addView(utilities, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.END or Gravity.BOTTOM,
        ))

        connectionPanel = sectionPanel().apply {
            visibility = View.VISIBLE
            addView(panelTitle("Conectar ao computador"))
            val usbRow = LinearLayout(context).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                addView(TextView(context).apply {
                    text = "USB oferece a menor latência"
                    setTextColor(getColor(R.color.deskink_muted))
                }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                connectButton = compactButton("Conectar USB") { connectUsb() }
                addView(connectButton)
            }
            addView(usbRow)
            val lanRow = LinearLayout(context).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                lanHostInput = EditText(context).apply {
                    hint = "IP do PC"
                    setText(getPreferences(MODE_PRIVATE).getString(LAN_HOST_PREFERENCE, ""))
                    inputType = InputType.TYPE_CLASS_TEXT
                    setSingleLine(true)
                }
                lanCodeInput = EditText(context).apply {
                    hint = "Código XXXX-XXXX"
                    inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
                    setSingleLine(true)
                }
                addView(lanHostInput, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.35f))
                addView(lanCodeInput, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                lanButton = compactButton("Conectar LAN") { connectLan() }
                addView(lanButton)
            }
            addView(lanRow)
            discoveryButton = compactButton("Buscar PCs na rede") { discoverLanHosts() }
            addView(discoveryButton)
        }

        shortcutPanel = sectionPanel().apply {
            visibility = View.GONE
            addView(panelTitle("Atalhos"))
            addView(shortcutRow(listOf(
                "Copiar" to SHORTCUT_COPY,
                "Colar" to SHORTCUT_PASTE,
                "Buscar" to SHORTCUT_FIND,
                "Página ↑" to SHORTCUT_PAGE_UP,
            )))
            addView(shortcutRow(listOf(
                "Página ↓" to SHORTCUT_PAGE_DOWN,
                "Play / Pausa" to SHORTCUT_MEDIA_PLAY_PAUSE,
                "Volume −" to SHORTCUT_VOLUME_DOWN,
                "Volume +" to SHORTCUT_VOLUME_UP,
            )))
        }

        settingsPanel = sectionPanel().apply {
            visibility = View.GONE
            addView(panelTitle("Ajustes avançados"))
            penModeButton = Button(context).apply { setOnClickListener { togglePenMode() } }
            addView(penModeButton)
            outputModeButton = Button(context).apply { setOnClickListener { selectNextOutputMode() } }
            addView(outputModeButton)
            overlayToolButton = Button(context).apply { setOnClickListener { selectNextOverlayTool() } }
            addView(overlayToolButton)
            overlayInteractionButton = Button(context).apply { setOnClickListener { toggleOverlayInteraction() } }
            addView(overlayInteractionButton)
            twoFingerGestureButton = Button(context).apply { setOnClickListener { selectNextTwoFingerGesture() } }
            addView(twoFingerGestureButton)
            inputDispatchButton = Button(context).apply { setOnClickListener { toggleInputDispatch() } }
            addView(inputDispatchButton)
            addView(compactButton("Abrir diagnóstico") { toggleExclusivePanel(diagnosticsPanel) })
        }

        diagnosticsPanel = sectionPanel().apply {
            visibility = View.GONE
            addView(panelTitle("Diagnóstico"))
            performanceText = diagnosticTextView()
            addView(performanceText)
            addView(LinearLayout(context).apply {
                orientation = LinearLayout.HORIZONTAL
                telemetryText = diagnosticTextView().also {
                    addView(it, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                }
                capabilityText = diagnosticTextView().also {
                    addView(it, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                }
            })
        }

        listOf(connectionPanel, shortcutPanel, settingsPanel, diagnosticsPanel).forEach { panel ->
            root.addView(panel, FrameLayout.LayoutParams(
                panelWidth,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.END,
            ).apply { topMargin = panelTop })
        }

        renderPenMode()
        renderMonitor()
        renderOutputMode()
        renderOverlayTool()
        renderOverlayInteraction()
        renderTwoFingerGesture()
        renderInputDispatch()
        renderMainMode()
        return root
    }

    private fun sectionPanel() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        val padding = (10 * resources.displayMetrics.density).toInt()
        setPadding(padding, padding, padding, padding)
        setBackgroundColor(getColor(R.color.deskink_surface))
    }

    private fun panelTitle(label: String) = TextView(this).apply {
        text = label
        textSize = 16f
        setTextColor(getColor(R.color.deskink_text))
        setTypeface(typeface, Typeface.BOLD)
    }

    private fun compactButton(label: String, action: () -> Unit) = Button(this).apply {
        text = label
        textSize = 13f
        minWidth = 0
        minHeight = 0
        setPadding(8, 4, 8, 4)
        setOnClickListener { action() }
    }

    private fun shortcutRow(shortcuts: List<Pair<String, Int>>) = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        shortcuts.forEach { (label, command) ->
            addView(
                compactButton(label) { sendShortcut(command) },
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
            )
        }
    }

    private fun toggleExclusivePanel(selected: View) {
        val show = selected.visibility != View.VISIBLE
        listOf(connectionPanel, shortcutPanel, settingsPanel, diagnosticsPanel).forEach {
            it.visibility = View.GONE
        }
        if (show) selected.visibility = View.VISIBLE
    }

    private fun diagnosticTextView() = TextView(this).apply {
        textSize = 13f
        setTextColor(getColor(R.color.deskink_text))
        typeface = Typeface.MONOSPACE
        setLineSpacing(0f, 1.1f)
    }

    private fun render(snapshot: StylusTelemetrySnapshot) {
        renderTransport()
        telemetryText.text = String.format(
            Locale.US,
            "Samples: %,d\nEvents: %,d\nHistorical: %,d\nCanceled: %,d\n" +
                "Action: %s\nTool: %s\nPointer: %d\n" +
                "X/Y: %.2f / %.2f\nPressure: %.4f\n" +
                "Tilt: %.4f rad\nOrientation: %.4f rad\nDistance: %.4f\n" +
                "Buttons: 0x%08X\nAction button: 0x%08X\nPalm canceled: %s",
            snapshot.sampleCount,
            snapshot.eventCount,
            snapshot.historicalSampleCount,
            snapshot.cancelCount,
            actionName(snapshot.action),
            toolName(snapshot.toolType),
            snapshot.pointerId,
            snapshot.x,
            snapshot.y,
            snapshot.pressure,
            snapshot.tiltRadians,
            snapshot.orientationRadians,
            snapshot.distance,
            snapshot.buttonState,
            snapshot.actionButton,
            snapshot.isCanceledByPalm,
        )

        val capabilities = snapshot.capabilities
        capabilityText.text = buildString {
            appendLine("Device: ${capabilities.deviceName}")
            appendLine("ID: ${capabilities.deviceId}")
            appendLine("Descriptor: ${capabilities.descriptor.take(24)}")
            appendLine("Pressure: ${axisText(capabilities.pressure)}")
            appendLine("Tilt: ${axisText(capabilities.tilt)}")
            appendLine("Orientation: ${axisText(capabilities.orientation)}")
            appendLine("Distance: ${axisText(capabilities.distance)}")
            appendLine("Hover: ${capabilities.hover}")
            appendLine("Side button: ${capabilities.sideButton}")
            append("Eraser: ${capabilities.eraser}")
        }
    }

    private fun connectUsb() {
        if (transport.state == TransportState.CONNECTING) return
        connectButton.isEnabled = false
        transportText.text = "USB: CONNECTING"
        connectionExecutor.execute {
            runCatching {
                transport.disconnect()
                transport = usbTransport
                transport.connect()
                monitorCount = transport.monitorCount
                val preferences = getPreferences(MODE_PRIVATE)
                selectedMonitorIndex = if (preferences.contains(MONITOR_PREFERENCE)) {
                    preferences.getInt(MONITOR_PREFERENCE, 0).coerceIn(0, monitorCount - 1)
                } else {
                    transport.selectedMonitorIndex
                }
                sendPenMode()
                sendMonitor()
                sendOutputMode()
                sendOverlayTool()
                sendOverlayMode()
            }
            handler.post {
                renderTransport()
                if (transport.state == TransportState.CONNECTED) connectionPanel.visibility = View.GONE
            }
        }
    }

    private fun connectLan() {
        if (transport.state == TransportState.CONNECTING) return
        val host = lanHostInput.text.toString().trim()
        val code = lanCodeInput.text.toString().trim()
        val preferences = getPreferences(MODE_PRIVATE)
        val savedFingerprint = preferences.getString(LAN_FINGERPRINT_PREFERENCE, null)
        val savedHost = preferences.getString(LAN_HOST_PREFERENCE, null)
        val trustedFingerprint = savedFingerprint?.takeIf {
            code.isEmpty() && (selectedDiscoveryFingerprint == it || host == savedHost)
        }
        connectButton.isEnabled = false
        lanButton.isEnabled = false
        transportText.text = "LAN: CONNECTING"
        connectionExecutor.execute {
            runCatching {
                transport.disconnect()
                val lanTransport = LanTransport(
                    host = host,
                    comparisonCode = code.takeIf { it.isNotEmpty() },
                    trustedFingerprint = trustedFingerprint,
                )
                transport = lanTransport
                lanTransport.connect()
                preferences.edit()
                    .putString(LAN_HOST_PREFERENCE, host)
                    .putString(LAN_FINGERPRINT_PREFERENCE, lanTransport.certificateFingerprintHex)
                    .apply()
                monitorCount = transport.monitorCount
                selectedMonitorIndex = getPreferences(MODE_PRIVATE)
                    .getInt(MONITOR_PREFERENCE, transport.selectedMonitorIndex)
                    .coerceIn(0, monitorCount - 1)
                sendPenMode()
                sendMonitor()
                sendOutputMode()
                sendOverlayTool()
                sendOverlayMode()
            }
            handler.post {
                renderTransport()
                if (transport.state == TransportState.CONNECTED) connectionPanel.visibility = View.GONE
            }
        }
    }

    private fun discoverLanHosts() {
        if (transport.state == TransportState.CONNECTING) return
        discoveryButton.isEnabled = false
        transportText.text = "LAN: BUSCANDO PCs..."
        connectionExecutor.execute {
            val result = runCatching { LanDiscoveryClient().discover() }
            handler.post {
                discoveryButton.isEnabled = true
                result.onSuccess { hosts -> showDiscoveredHosts(hosts) }
                    .onFailure { failure ->
                        transportText.text = "Discovery error=${failure.message ?: failure.javaClass.simpleName}"
                    }
            }
        }
    }

    private fun showDiscoveredHosts(hosts: List<DiscoveredDeskInkHost>) {
        when (hosts.size) {
            0 -> transportText.text = "Nenhum PC DeskInk encontrado na rede"
            1 -> selectDiscoveredHost(hosts.single())
            else -> AlertDialog.Builder(this)
                .setTitle("Escolha o PC DeskInk")
                .setItems(hosts.map { "${it.hostName} — ${it.address} — ${it.comparisonCode}" }.toTypedArray()) {
                        _, index -> selectDiscoveredHost(hosts[index])
                }
                .setNegativeButton("Cancelar", null)
                .show()
        }
    }

    private fun selectDiscoveredHost(host: DiscoveredDeskInkHost) {
        selectedDiscoveryFingerprint = host.certificateFingerprintHex
        lanHostInput.setText(host.address)
        lanCodeInput.text.clear()
        val trusted = getPreferences(MODE_PRIVATE)
            .getString(LAN_FINGERPRINT_PREFERENCE, null) == host.certificateFingerprintHex
        lanCodeInput.hint = if (trusted) "PC confiável — código não necessário" else {
            "Confira e digite ${host.comparisonCode}"
        }
        transportText.text = if (trusted) {
            "PC confiável: ${host.hostName} (${host.address})\nToque em Connect LAN"
        } else {
            "Encontrado: ${host.hostName} (${host.address})\nConfira o código no PC"
        }
        getPreferences(MODE_PRIVATE).edit().putString(LAN_HOST_PREFERENCE, host.address).apply()
    }

    private fun togglePenMode() {
        penScrollEnabled = !penScrollEnabled
        getPreferences(MODE_PRIVATE)
            .edit()
            .putBoolean(PEN_SCROLL_PREFERENCE, penScrollEnabled)
            .apply()
        renderPenMode()
        connectionExecutor.execute {
            if (transport.state == TransportState.CONNECTED) {
                runCatching { sendPenMode() }
                handler.post { renderTransport() }
            }
        }
    }

    private fun sendPenMode() {
        transport.sendControl(
            ControlProtocolV1.encodeSetMouseMode(transport.sessionId, penScrollEnabled),
        )
    }

    private fun selectNextMonitor() {
        if (transport.state != TransportState.CONNECTED || monitorCount <= 1) return
        selectedMonitorIndex = (selectedMonitorIndex + 1) % monitorCount
        getPreferences(MODE_PRIVATE)
            .edit()
            .putInt(MONITOR_PREFERENCE, selectedMonitorIndex)
            .apply()
        renderMonitor()
        connectionExecutor.execute {
            runCatching { sendMonitor() }
            handler.post { renderTransport() }
        }
    }

    private fun sendMonitor() {
        transport.sendControl(
            ControlProtocolV1.encodeSetMonitor(transport.sessionId, selectedMonitorIndex),
        )
    }

    private fun selectNextOutputMode() {
        outputMode = (outputMode + 1) % 3
        getPreferences(MODE_PRIVATE).edit()
            .putInt(OUTPUT_MODE_PREFERENCE, outputMode)
            .apply()
        renderOutputMode()
        connectionExecutor.execute {
            if (transport.state == TransportState.CONNECTED) runCatching { sendOutputMode() }
            handler.post { renderTransport() }
        }
    }

    private fun sendOutputMode() {
        transport.sendControl(ControlProtocolV1.encodeSetOutputMode(transport.sessionId, outputMode))
    }

    private fun renderOutputMode() {
        if (!::outputModeButton.isInitialized) return
        outputModeButton.text = when (outputMode) {
            0 -> "Saida: MOUSE"
            1 -> "Saida: CANETA WINDOWS"
            else -> "Saida: OVERLAY"
        }
        renderMainMode()
    }

    private fun selectNextOverlayTool() {
        overlayTool = (overlayTool + 1) % 3
        getPreferences(MODE_PRIVATE).edit().putInt(OVERLAY_TOOL_PREFERENCE, overlayTool).apply()
        renderOverlayTool()
        sendControlIfConnected { sendOverlayTool() }
    }

    private fun sendOverlayTool() {
        transport.sendControl(ControlProtocolV1.encodeSetOverlayTool(transport.sessionId, overlayTool))
    }

    private fun renderOverlayTool() {
        if (!::overlayToolButton.isInitialized) return
        overlayToolButton.text = when (overlayTool) {
            0 -> "Ferramenta: CANETA"
            1 -> "Ferramenta: MARCA-TEXTO"
            else -> "Ferramenta: BORRACHA"
        }
        renderMainMode()
    }

    private fun toggleOverlayInteraction() {
        overlayInteractive = !overlayInteractive
        renderOverlayInteraction()
        sendControlIfConnected { sendOverlayMode() }
    }

    private fun sendOverlayMode() {
        transport.sendControl(ControlProtocolV1.encodeSetOverlayMode(
            transport.sessionId,
            if (overlayInteractive) 2 else 1,
        ))
    }

    private fun renderOverlayInteraction() {
        if (!::overlayInteractionButton.isInitialized) return
        overlayInteractionButton.text = if (overlayInteractive) {
            "Overlay: INTERATIVO (bloqueia cliques abaixo)"
        } else {
            "Overlay: CLICK-THROUGH"
        }
    }

    private fun selectNextTwoFingerGesture() {
        twoFingerGesture = (twoFingerGesture + 1) % 3
        getPreferences(MODE_PRIVATE).edit()
            .putInt(TWO_FINGER_GESTURE_PREFERENCE, twoFingerGesture)
            .apply()
        renderTwoFingerGesture()
    }

    private fun renderTwoFingerGesture() {
        if (!::twoFingerGestureButton.isInitialized) return
        twoFingerGestureButton.text = when (twoFingerGesture) {
            0 -> "Dois dedos: DESLIGADO"
            1 -> "Dois dedos: TOQUE PARA DESFAZER"
            else -> "Dois dedos: TOQUE ALTERNA CANETA / MARCA-TEXTO"
        }
    }

    private fun performTwoFingerGesture() {
        when (twoFingerGesture) {
            0 -> Unit
            1 -> sendControlIfConnected {
                transport.sendControl(ControlProtocolV1.encodeOverlayCommand(transport.sessionId, 0))
            }
            2 -> {
                overlayTool = if (overlayTool == 1) 0 else 1
                getPreferences(MODE_PRIVATE).edit()
                    .putInt(OVERLAY_TOOL_PREFERENCE, overlayTool)
                    .apply()
                renderOverlayTool()
                sendControlIfConnected { sendOverlayTool() }
            }
        }
    }

    private fun toggleInputDispatch() {
        unbufferedDispatchEnabled = !unbufferedDispatchEnabled
        getPreferences(MODE_PRIVATE).edit()
            .putBoolean(UNBUFFERED_DISPATCH_PREFERENCE, unbufferedDispatchEnabled)
            .apply()
        renderInputDispatch()
    }

    private fun renderInputDispatch() {
        if (!::inputDispatchButton.isInitialized) return
        inputDispatchButton.text = if (unbufferedDispatchEnabled) {
            "Entrada: LOW LATENCY (sem buffering)"
        } else {
            "Entrada: COMPATÍVEL (com batching)"
        }
    }

    private fun overlayCommandButton(label: String, command: Int) = compactButton(label) {
        sendControlIfConnected {
            transport.sendControl(ControlProtocolV1.encodeOverlayCommand(transport.sessionId, command))
        }
    }

    private fun sendShortcut(command: Int) {
        sendControlIfConnected {
            transport.sendControl(ControlProtocolV1.encodeExecuteShortcut(transport.sessionId, command))
        }
    }

    private fun selectMainMode(mode: Int) {
        when (mode) {
            MAIN_MODE_CURSOR -> {
                outputMode = 0
                penScrollEnabled = false
            }
            MAIN_MODE_SCROLL -> {
                outputMode = 0
                penScrollEnabled = true
            }
            MAIN_MODE_PEN -> outputMode = 1
            MAIN_MODE_OVERLAY -> {
                outputMode = 2
                overlayTool = 0
            }
            MAIN_MODE_HIGHLIGHTER -> {
                outputMode = 2
                overlayTool = 1
            }
            MAIN_MODE_ERASER -> {
                outputMode = 2
                overlayTool = 2
            }
            else -> return
        }
        getPreferences(MODE_PRIVATE).edit()
            .putBoolean(PEN_SCROLL_PREFERENCE, penScrollEnabled)
            .putInt(OUTPUT_MODE_PREFERENCE, outputMode)
            .putInt(OVERLAY_TOOL_PREFERENCE, overlayTool)
            .apply()
        renderPenMode()
        renderOutputMode()
        renderOverlayTool()
        renderMainMode()
        sendControlIfConnected {
            sendPenMode()
            sendOutputMode()
            sendOverlayTool()
        }
    }

    private fun renderMainMode() {
        if (mainModeButtons.isEmpty()) return
        val selectedMode = when (outputMode) {
            0 -> if (penScrollEnabled) MAIN_MODE_SCROLL else MAIN_MODE_CURSOR
            1 -> MAIN_MODE_PEN
            else -> when (overlayTool) {
                0 -> MAIN_MODE_OVERLAY
                1 -> MAIN_MODE_HIGHLIGHTER
                else -> MAIN_MODE_ERASER
            }
        }
        mainModeButtons.forEach { (mode, button) ->
            val selected = mode == selectedMode
            button.backgroundTintList = ColorStateList.valueOf(
                getColor(if (selected) R.color.deskink_accent else R.color.deskink_surface),
            )
            button.setTextColor(getColor(if (selected) R.color.deskink_background else R.color.deskink_text))
        }
    }

    private fun sendControlIfConnected(action: () -> Unit) {
        connectionExecutor.execute {
            if (transport.state == TransportState.CONNECTED) runCatching(action)
            handler.post { renderTransport() }
        }
    }

    private fun renderPenMode() {
        penModeButton.text = if (penScrollEnabled) {
            "Caneta: SCROLL (permanece ativo)"
        } else {
            "Caneta: CLIQUE / ARRASTE"
        }
        renderMainMode()
    }

    private fun renderMonitor() {
        if (!::monitorButton.isInitialized) return
        monitorButton.text = if (monitorCount > 0) {
            "Monitor ${selectedMonitorIndex + 1}/$monitorCount"
        } else {
            "Monitor"
        }
        monitorButton.isEnabled = transport.state == TransportState.CONNECTED && monitorCount > 1
    }

    private fun renderTransport() {
        if (!::transportText.isInitialized) return
        val metrics = transport.metrics
        val performance = packetPublisher.performanceSnapshot()
        val diagnostic = buildString {
            append("${if (transport === usbTransport) "USB" else "LAN"}: ${transport.state}")
            if (transport.sessionId != 0L) append("  session=${transport.sessionId.toString(16)}")
            appendLine()
            append("frames=${metrics.inputFramesSent} bytes=${metrics.bytesSent}")
            append(" dropped=${packetPublisher.droppedFrameCount()}")
            metrics.controlRttUs?.let { append(" rtt=${String.format(Locale.US, "%.2f", it / 1000.0)}ms") }
            metrics.clockUncertaintyUs?.let { append(" ±${String.format(Locale.US, "%.2f", it / 1000.0)}ms") }
            if (performance.prepare != null) {
                appendLine()
                append("us prep=${timingText(performance.prepare)}")
                append(" enc=${timingText(performance.encode)}")
                append(" queue=${timingText(performance.queue)}")
                append(" send=${timingText(performance.send)}")
                append(" maxQ=${performance.maximumQueueDepth}")
            }
            transport.lastError?.let { appendLine().append("error=$it") }
        }
        if (::performanceText.isInitialized) performanceText.text = diagnostic
        transportText.text = buildString {
            append(if (transport === usbTransport) "USB" else "LAN")
            append(" • ${transportStateLabel(transport.state)}")
            metrics.controlRttUs?.let {
                append(" • ${String.format(Locale.US, "%.1f", it / 1000.0)} ms")
            }
            transport.lastError?.let { append(" • $it") }
        }
        connectButton.isEnabled = transport.state != TransportState.CONNECTING
        lanButton.isEnabled = transport.state != TransportState.CONNECTING
        discoveryButton.isEnabled = transport.state != TransportState.CONNECTING
        connectButton.text = if (transport === usbTransport && transport.state == TransportState.CONNECTED) {
            "Reconectar USB"
        } else {
            "Conectar USB"
        }
        lanButton.text = if (transport !== usbTransport && transport.state == TransportState.CONNECTED) {
            "Reconectar LAN"
        } else {
            "Conectar LAN"
        }
        renderMonitor()
    }

    private fun transportStateLabel(state: TransportState): String = when (state) {
        TransportState.DISCONNECTED -> "desconectado"
        TransportState.CONNECTING -> "conectando"
        TransportState.CONNECTED -> "conectado"
        TransportState.ERROR -> "erro"
    }

    private fun timingText(timing: com.deskink.android.input.TimingPercentiles?): String =
        timing?.let { "${it.p50Us}/${it.p95Us}/${it.p99Us}" } ?: "n/a"

    private fun axisText(axis: AxisCapability): String = when {
        axis.minimum != null && axis.maximum != null -> String.format(
            Locale.US,
            "%s [%.3f..%.3f]",
            axis.state,
            axis.minimum,
            axis.maximum,
        )
        else -> axis.state.toString()
    }

    private fun actionName(action: Int): String = when (action) {
        MotionEvent.ACTION_DOWN -> "DOWN"
        MotionEvent.ACTION_UP -> "UP"
        MotionEvent.ACTION_MOVE -> "MOVE"
        MotionEvent.ACTION_CANCEL -> "CANCEL"
        MotionEvent.ACTION_POINTER_DOWN -> "POINTER_DOWN"
        MotionEvent.ACTION_POINTER_UP -> "POINTER_UP"
        MotionEvent.ACTION_HOVER_ENTER -> "HOVER_ENTER"
        MotionEvent.ACTION_HOVER_MOVE -> "HOVER_MOVE"
        MotionEvent.ACTION_HOVER_EXIT -> "HOVER_EXIT"
        MotionEvent.ACTION_BUTTON_PRESS -> "BUTTON_PRESS"
        MotionEvent.ACTION_BUTTON_RELEASE -> "BUTTON_RELEASE"
        else -> action.toString()
    }

    private fun toolName(tool: Int): String = when (tool) {
        MotionEvent.TOOL_TYPE_FINGER -> "FINGER"
        MotionEvent.TOOL_TYPE_STYLUS -> "STYLUS"
        MotionEvent.TOOL_TYPE_ERASER -> "ERASER"
        MotionEvent.TOOL_TYPE_MOUSE -> "MOUSE"
        else -> "UNKNOWN"
    }

    companion object {
        private const val MAIN_MODE_CURSOR = 0
        private const val MAIN_MODE_SCROLL = 1
        private const val MAIN_MODE_PEN = 2
        private const val MAIN_MODE_OVERLAY = 3
        private const val MAIN_MODE_HIGHLIGHTER = 4
        private const val MAIN_MODE_ERASER = 5
        private const val SHORTCUT_COPY = 0
        private const val SHORTCUT_PASTE = 1
        private const val SHORTCUT_FIND = 2
        private const val SHORTCUT_PAGE_UP = 3
        private const val SHORTCUT_PAGE_DOWN = 4
        private const val SHORTCUT_MEDIA_PLAY_PAUSE = 5
        private const val SHORTCUT_VOLUME_DOWN = 6
        private const val SHORTCUT_VOLUME_UP = 7
        private const val TELEMETRY_REFRESH_MS = 100L
        private const val PEN_SCROLL_PREFERENCE = "pen_scroll_enabled"
        private const val MONITOR_PREFERENCE = "selected_monitor_index"
        private const val OUTPUT_MODE_PREFERENCE = "output_mode"
        private const val OVERLAY_TOOL_PREFERENCE = "overlay_tool"
        private const val TWO_FINGER_GESTURE_PREFERENCE = "two_finger_gesture"
        private const val UNBUFFERED_DISPATCH_PREFERENCE = "unbuffered_dispatch"
        private const val LAN_HOST_PREFERENCE = "lan_host"
        private const val LAN_FINGERPRINT_PREFERENCE = "lan_fingerprint"
    }
}
