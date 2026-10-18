#include <FastLED.h>

// ==========================================
// --- HARDWARE & SYSTEM CONFIGURATION ---
// Baud Rate: 115200 | Target Port: COM8
// ==========================================

#define PIN_MAIN    6        
#define PIN_TIMER   7
#define MAIN_NUM_LEDS    100
#define TIMER_NUM_LEDS   60
#define LED_TYPE    WS2812B
#define COLOR_ORDER GRB

#define MAX_POWER_MILLIAMPS 400  
#define MAX_VOLTS           5

CRGB mainLeds[MAIN_NUM_LEDS];
CRGB timerLeds[TIMER_NUM_LEDS];

// ==========================================
// --- EVENT DURATIONS (in milliseconds) ---
// ==========================================
const unsigned long COL_DURATION = 500;  // Collision: 0.5 sec
const unsigned long BST_DURATION = 1500; // Boost: 1.5 sec
const unsigned long DNG_DURATION = 2000; // Danger: 2.0 sec
const unsigned long WIN_DURATION = 3000; // Victory: 3.0 sec
const unsigned long RNG_DURATION = 1200; // Ring Passed: 1.2 sec

// ==========================================
// --- SERIAL PARSER BUFFER ---
// ==========================================
const byte numChars = 32;
char receivedChars[numChars];
boolean newData = false;

// ==========================================
// --- STATE MACHINE VARIABLES ---
// ==========================================
// Layer 1: Base State
int currentSpeed = 0; // 0 to 100

// Layer 2: Time Bar Overlay
bool isTimerActive = false;
bool sessionTimerStarted = false;
unsigned long timerStartTime = 0;
const int timerDurationSec = 60;

// Layer 3: Event State
enum EventType { EVENT_NONE, EVENT_COL, EVENT_BST, EVENT_DNG, EVENT_WIN, EVENT_RNG };
EventType activeEvent = EVENT_NONE;
unsigned long eventStartTime = 0;

// Timing & Frame Rate
unsigned long currentMillis = 0;
unsigned long lastFrameTime = 0;
const int FRAME_INTERVAL = 25; // ~40 FPS target

void setup() {
    Serial.begin(115200);
    // Wait for Serial to be ready
    while (!Serial) { ; }
    delay(500); 

    Serial.println(F("======================================="));
    Serial.println(F(" VR Event-to-Visual Mapping System"));
    Serial.println(F(" Hardware: Arduino Uno | Port: COM8"));
    Serial.println(F("======================================="));

    FastLED.addLeds<LED_TYPE, PIN_MAIN, COLOR_ORDER>(mainLeds, MAIN_NUM_LEDS)
           .setCorrection(TypicalLEDStrip);
    FastLED.addLeds<LED_TYPE, PIN_TIMER, COLOR_ORDER>(timerLeds, TIMER_NUM_LEDS)
           .setCorrection(TypicalLEDStrip);
    
    FastLED.setMaxPowerInVoltsAndMilliamps(MAX_VOLTS, MAX_POWER_MILLIAMPS);
    
    Serial.println(F("System Ready. Waiting for Unity commands..."));
}

void loop() {
    currentMillis = millis();
    
    // 1. Constantly read Serial (Non-blocking)
    recvWithEndMarker();
    
    // 2. Parse if new command arrived
    if (newData) {
        parseData();
        newData = false;
    }

    // 3. Check if active event has expired
    checkEventExpiration();

    // 4. Render the visual mapping at a controlled frame rate
    if (currentMillis - lastFrameTime >= FRAME_INTERVAL) {
        lastFrameTime = currentMillis;
        updateLEDs();
    }
}

// ==========================================
// --- SERIAL PARSING (MEMORY SAFE) ---
// ==========================================
void recvWithEndMarker() {
    static byte ndx = 0;
    char endMarker = '\n';
    char rc;

    while (Serial.available() > 0 && newData == false) {
        rc = Serial.read();

        // Ignore carriage returns in case Unity sends \r\n
        if (rc == '\r') continue;

        if (rc != endMarker) {
            receivedChars[ndx] = rc;
            ndx++;
            if (ndx >= numChars) {
                ndx = numChars - 1; // Prevent buffer overflow
            }
        } else {
            receivedChars[ndx] = '\0'; // Terminate the C-string
            ndx = 0;
            newData = true;
        }
    }
}

// Helper to strictly check if a string contains only digits
bool isNumeric(const char* str) {
    if (str[0] == '\0') return false; // Empty string
    for (int i = 0; str[i] != '\0'; i++) {
        if (!isdigit(str[i])) {
            return false;
        }
    }
    return true;
}

void parseData() {
    // Ignore empty or very short commands
    if (strlen(receivedChars) < 5) return; 

    // Check Continuous State: "C:SPD:XX"
    if (strncmp(receivedChars, "C:SPD:", 6) == 0) {
        const char* valStr = &receivedChars[6];
        while (*valStr == ' ' || *valStr == '\t') valStr++;
        
        if (isNumeric(valStr)) {
            int spd = atoi(valStr);
            currentSpeed = constrain(spd, 0, 100);
            
            if (currentSpeed > 0 && !sessionTimerStarted) {
                sessionTimerStarted = true;
                isTimerActive = true;
                timerStartTime = millis();
                Serial.println(F("[TIMER] First movement detected"));
                Serial.println(F("[TIMER] Started - 60 seconds"));
            }

            Serial.print(F("Base State Updated -> Speed: "));
            Serial.println(currentSpeed);
        } else {
            Serial.print(F("Debug: Rejected Malformed Speed -> "));
            Serial.println(valStr);
        }
    } 
    // Check Discrete Event: "E:XXX"
    else if (strncmp(receivedChars, "E:", 2) == 0) {
        char eventCode[4];
        strncpy(eventCode, &receivedChars[2], 3);
        eventCode[3] = '\0';
        
        if (strcmp(eventCode, "COL") == 0) {
            triggerEvent(EVENT_COL);
            Serial.println(F("Event Triggered -> COLLISION"));
        } 
        else if (strcmp(eventCode, "BST") == 0) {
            triggerEvent(EVENT_BST);
            Serial.println(F("Event Triggered -> BOOST"));
        } 
        else if (strcmp(eventCode, "DNG") == 0) {
            triggerEvent(EVENT_DNG);
            Serial.println(F("Event Triggered -> DANGER"));
        } 
        else if (strcmp(eventCode, "WIN") == 0) {
            triggerEvent(EVENT_WIN);
            Serial.println(F("Event Triggered -> VICTORY"));
        } 
        else if (strcmp(eventCode, "RNG") == 0) {
            triggerEvent(EVENT_RNG);
            Serial.println(F("Event Triggered -> RING PASSED"));
        }
        else if (strcmp(eventCode, "TMR") == 0) {
            isTimerActive = true;
            sessionTimerStarted = true;
            timerStartTime = currentMillis;
            Serial.println(F("[TIMER] Started: 60 seconds (Manual)"));
        }
        else if (strcmp(eventCode, "TMS") == 0) {
            isTimerActive = false;
            for (int i = 0; i < TIMER_NUM_LEDS; i++) {
                timerLeds[i] = CRGB::Black;
            }
            Serial.println(F("[TIMER] Stopped and Cleared"));
        }
        else if (strcmp(eventCode, "RST") == 0) {
            sessionTimerStarted = false;
            isTimerActive = false;
            for (int i = 0; i < TIMER_NUM_LEDS; i++) {
                timerLeds[i] = CRGB::Black;
            }
            Serial.println(F("[TIMER] Session reset - waiting for movement"));
        }
        else {
            Serial.print(F("Debug: Unknown Event Code -> "));
            Serial.println(eventCode);
        }
    } 
    else {
        Serial.print(F("Debug: Unknown Command Format -> "));
        Serial.println(receivedChars);
    }
}

void triggerEvent(EventType type) {
    // 1. Strict Priority: Ring Passed (EVENT_RNG) cannot be interrupted by Boost!
    if (activeEvent == EVENT_RNG && type == EVENT_BST) {
        Serial.println(F("[PRIORITY] Ring Passed active -> Boost ignored"));
        return;
    }
    // 2. Collision (EVENT_COL) cannot be interrupted by Boost
    if (activeEvent == EVENT_COL && type == EVENT_BST) {
        return;
    }

    activeEvent = type;
    eventStartTime = currentMillis; // Record exactly when it started
}

void checkEventExpiration() {
    if (activeEvent == EVENT_NONE) return;

    unsigned long duration = 0;
    switch(activeEvent) {
        case EVENT_COL: duration = COL_DURATION; break;
        case EVENT_BST: duration = BST_DURATION; break;
        case EVENT_DNG: duration = DNG_DURATION; break;
        case EVENT_WIN: duration = WIN_DURATION; break;
        case EVENT_RNG: duration = RNG_DURATION; break;
    }

    if (currentMillis - eventStartTime >= duration) {
        activeEvent = EVENT_NONE;
        Serial.println(F("Event Expired -> Returning to Base State"));
    }
}

// ==========================================
// --- EVENT-TO-VISUAL MAPPING LAYER ---
// ==========================================
void updateLEDs() {
    // 1. Base Speed Layer OR Discrete Event Layer
    // When an event is active (like Ring Passed or Collision), it gets 100% EXCLUSIVE priority!
    if (activeEvent != EVENT_NONE) {
        switch(activeEvent) {
            case EVENT_COL: runCollisionEffect(); break;
            case EVENT_BST: runBoostEffect(); break;
            case EVENT_DNG: runDangerEffect(); break;
            case EVENT_WIN: runVictoryEffect(); break;
            case EVENT_RNG: runRingEffect(); break;
        }
    } else {
        // Only run continuous speed/boost animation when no discrete event is playing
        runSpeedAnimation();
    }

    // 2. Time Bar Layer
    if (isTimerActive) {
        unsigned long elapsedTimer = currentMillis - timerStartTime;
        int secondsPassed = elapsedTimer / 1000;
        
        static int lastSecondsLogged = -1;
        if (secondsPassed != lastSecondsLogged) {
            if (secondsPassed == 15) Serial.println(F("[TIMER] Remaining: 45"));
            else if (secondsPassed == 30) Serial.println(F("[TIMER] Remaining: 30"));
            else if (secondsPassed == 50) Serial.println(F("[TIMER] Remaining: 10"));
            
            lastSecondsLogged = secondsPassed;
        }

        if (secondsPassed >= timerDurationSec) {
            isTimerActive = false;
            Serial.println(F("[TIMER] Complete"));
            for (int i = 0; i < TIMER_NUM_LEDS; i++) {
                timerLeds[i] = CRGB::Black;
            }
        } else {
            int remainingSeconds = timerDurationSec - secondsPassed;
            int ledsToTurnOff = secondsPassed;
            
            // Determine if we are in urgent mode and currently flashing OFF
            bool urgentFlashOff = false;
            if (remainingSeconds <= 20) {
                // Sync flash cycle to start predictably ON exactly when urgent mode begins (40,000 ms elapsed)
                unsigned long urgentElapsed = elapsedTimer - ((timerDurationSec - 20) * 1000UL);
                if ((urgentElapsed % 600) >= 300) {
                    urgentFlashOff = true;
                }
            }

            for (int i = 0; i < TIMER_NUM_LEDS; i++) {
                // 1. Handle normal countdown turn off
                if (i >= TIMER_NUM_LEDS - ledsToTurnOff) {
                    timerLeds[i] = CRGB::Black;
                } 
                // 2. Handle remaining LEDs
                else {
                    if (urgentFlashOff) {
                        timerLeds[i] = CRGB::Black;
                    } else {
                        timerLeds[i] = CRGB::Green;
                    }
                }
            }
        }
    }

    // 3. Show
    FastLED.show();
}

// --- CONTINUOUS: SPEED / DYNAMIC BOOST LIGHTS ---
void runSpeedAnimation() {
    if (currentSpeed <= 0) {
        fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, 50); 
        return;
    }
    
    // Trail fade: faster fade at higher speeds for sharp boost streaks
    uint8_t fadeSpeed = map(currentSpeed, 1, 100, 35, 80);
    fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, fadeSpeed);
    
    static float virtualPhase = 0.0;
    
    // Dynamic Speed Mapping:
    // Speed 1   -> ~0.4 pixels per frame (~16 px/sec) - gentle wind stream
    // Speed 50  -> ~2.5 pixels per frame (~100 px/sec) - steady boost stream
    // Speed 100 -> ~6.0 pixels per frame (~240 px/sec) - high velocity boost streak!
    float speedRatio = ((float)currentSpeed / 100.0f);
    float increment = 0.35f + (speedRatio * speedRatio * 5.65f);
    virtualPhase += increment;
    if (virtualPhase >= 10000.0f) virtualPhase -= 10000.0f;
    
    // Interleaved boost streams (2 streams at medium speed, 3 streams at high speed)
    int streamCount = (currentSpeed > 60) ? 3 : 2;
    int spacing = MAIN_NUM_LEDS / streamCount;
    
    for (int s = 0; s < streamCount; s++) {
        int headPos = ((int)(virtualPhase + s * spacing)) % MAIN_NUM_LEDS;
        // Move backwards (99 -> 0) so boost energy rushes toward/past player as they fly forward
        int pos = MAIN_NUM_LEDS - 1 - headPos;
        
        if (pos >= 0 && pos < MAIN_NUM_LEDS) {
            // Alternating Cyan and White boost spark head
            CRGB headColor = ((currentMillis / 40 + s) % 2 == 0) ? CRGB::Cyan : CRGB::White;
            mainLeds[pos] = headColor;
            
            // Trailing cyan/sky-blue tail that lengthens at higher speed
            if (currentSpeed > 20 && pos + 1 < MAIN_NUM_LEDS) {
                mainLeds[pos + 1] = CRGB(0, 180, 255); // Vibrant Cyan-Blue
            }
            if (currentSpeed > 55 && pos + 2 < MAIN_NUM_LEDS) {
                mainLeds[pos + 2] = CRGB(0, 90, 200);  // Deep trailing glow
            }
        }
    }
}

// --- DISCRETE: COLLISION ---
void runCollisionEffect() {
    unsigned long elapsed = currentMillis - eventStartTime;
    uint8_t fadeAmount = map(elapsed, 0, COL_DURATION, 0, 255);
    
    fill_solid(mainLeds, MAIN_NUM_LEDS, CRGB::Red);
    fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, fadeAmount); 
}

// --- DISCRETE: BOOST ---
void runBoostEffect() {
    unsigned long elapsed = currentMillis - eventStartTime;
    
    fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, 60); 
    
    int forwardPos = (elapsed * MAIN_NUM_LEDS * 3) / BST_DURATION; 
    forwardPos = forwardPos % MAIN_NUM_LEDS;
    int pos = MAIN_NUM_LEDS - 1 - forwardPos; // Match direction 99 -> 0
    
    if (pos >= 0 && pos < MAIN_NUM_LEDS) {
        if ((elapsed / 30) % 2 == 0) {
            mainLeds[pos] = CRGB::Cyan;
        } else {
            mainLeds[pos] = CRGB::White;
        }
    }
}

// --- DISCRETE: DANGER ---
void runDangerEffect() {
    uint8_t breath = beatsin8(45, 20, 255); 
    
    fill_solid(mainLeds, MAIN_NUM_LEDS, CRGB::Red);
    fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, 255 - breath); 
}

// --- DISCRETE: VICTORY ---
void runVictoryEffect() {
    unsigned long elapsed = currentMillis - eventStartTime;
    uint8_t hue = (elapsed / 2) % 255;
    
    fill_rainbow(mainLeds, MAIN_NUM_LEDS, hue, 10);
}

// --- DISCRETE: RING PASSED ---
void runRingEffect() {
    unsigned long elapsed = currentMillis - eventStartTime;
    CRGB ringColor = CRGB::Gold; 
    
    if (elapsed < 200) {
        fill_solid(mainLeds, MAIN_NUM_LEDS, ringColor);
    } 
    else if (elapsed < 800) {
        // Fade the existing pixels to create the tail
        fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, 60); 
        
        unsigned long waveElapsed = elapsed - 200;
        // Calculate the head of the comet (now moving backwards 99 -> 0)
        int forwardPos = (waveElapsed * MAIN_NUM_LEDS) / 600; 
        int pos = MAIN_NUM_LEDS - 1 - forwardPos;
        
        if (pos >= 0 && pos < MAIN_NUM_LEDS) {
            mainLeds[pos] = ringColor;
            
            // Draw a few extra pixels for a thicker head, trailing backwards (higher indices)
            if (pos + 1 < MAIN_NUM_LEDS) mainLeds[pos + 1] = ringColor;
            if (pos + 2 < MAIN_NUM_LEDS) mainLeds[pos + 2] = CRGB(255, 140, 0); // Amber/Gold mix
            if (pos + 3 < MAIN_NUM_LEDS) mainLeds[pos + 3] = CRGB(200, 100, 0);
        }
    } 
    else {
        unsigned long glowElapsed = elapsed - 800;
        uint8_t fadeAmount = map(glowElapsed, 0, 400, 0, 255);
        
        fill_solid(mainLeds, MAIN_NUM_LEDS, ringColor);
        fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, fadeAmount); 
    }
}

