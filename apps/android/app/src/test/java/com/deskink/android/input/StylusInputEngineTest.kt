package com.deskink.android.input

import android.view.MotionEvent
import org.junit.Assert.assertEquals
import org.junit.Test

class StylusInputEngineTest {
    @Test
    fun nonActionPointerRemainsMoveDuringPointerDown() {
        assertEquals(
            MotionEvent.ACTION_MOVE,
            StylusInputEngine.semanticActionFor(
                MotionEvent.ACTION_POINTER_DOWN,
                pointerIndex = 0,
                actionIndex = 1,
            ),
        )
    }

    @Test
    fun actionPointerKeepsPointerUpTransition() {
        assertEquals(
            MotionEvent.ACTION_POINTER_UP,
            StylusInputEngine.semanticActionFor(
                MotionEvent.ACTION_POINTER_UP,
                pointerIndex = 1,
                actionIndex = 1,
            ),
        )
    }

    @Test
    fun hoverHistoryIsClassifiedAsHoverMove() {
        assertEquals(
            MotionEvent.ACTION_HOVER_MOVE,
            StylusInputEngine.historicalActionFor(MotionEvent.ACTION_HOVER_EXIT),
        )
    }

    @Test
    fun touchHistoryIsClassifiedAsMove() {
        assertEquals(
            MotionEvent.ACTION_MOVE,
            StylusInputEngine.historicalActionFor(MotionEvent.ACTION_UP),
        )
    }
}
