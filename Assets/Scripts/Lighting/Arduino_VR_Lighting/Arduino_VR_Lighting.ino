#include <FastLED.h>

// ==========================================
// --- HARDWARE & SYSTEM CONFIGURATION ---
// ==========================================\

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
    Serial.println(F(" Hardware: Arduino Uno"));
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
    // 1. Base Speed Layer
    runSpeedAnimation();

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

    // 3. Temporary Event Layer LAST
    if (activeEvent != EVENT_NONE) {
        switch(activeEvent) {
            case EVENT_COL: runCollisionEffect(); break;
            case EVENT_BST: runBoostEffect(); break;
            case EVENT_DNG: runDangerEffect(); break;
            case EVENT_WIN: runVictoryEffect(); break;
            case EVENT_RNG: runRingEffect(); break;
        }
    }
    
    // 4. Show
    FastLED.show();
}

// --- CONTINUOUS: SPEED ---
void runSpeedAnimation() {
    if (currentSpeed == 0) {
        fadeToBlackBy(mainLeds, MAIN_NUM_LEDS, 60); 
        return;
    }
    
    for(int i = 0; i < MAIN_NUM_LEDS; i++) {
        mainLeds[i].r = scale8(mainLeds[i].r, 120); 
        mainLeds[i].g = scale8(mainLeds[i].g, 120); 
        mainLeds[i].b = scale8(mainLeds[i].b, 220); 
    }
    
    static float virtualPhase = 0.0;
    
    // Smooth speed mapping: 
    // Speed 1 -> ~0.05 pixels per frame
    // Speed 100 -> ~0.60 pixels per frame
    float increment = map((long)currentSpeed, 1, 100, 5, 60) / 100.0; 
    virtualPhase += increment;
    int pos = MAIN_NUM_LEDS - 1 - ((int)virtualPhase % MAIN_NUM_LEDS);
    
    mainLeds[pos] = CRGB::White;
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
    
    int pos = (elapsed * MAIN_NUM_LEDS * 3) / BST_DURATION; 
    pos = pos % MAIN_NUM_LEDS;
    
    if ((elapsed / 30) % 2 == 0) {
        mainLeds[pos] = CRGB::Cyan;
    } else {
        mainLeds[pos] = CRGB::White;
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
