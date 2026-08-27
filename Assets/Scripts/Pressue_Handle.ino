// Component 01: Motion Rendering - Pressure Sensor Test
const int fsrPin = A0; // The signal leg of the FSR + Resistor
int rawValue = 0;
int mappedSpeed = 0;

void setup() {
  Serial.begin(9600);
  Serial.println("--- FSR ONLY MODE: TESTING THRUST ---");
}

void loop() {
  // 1. Read the raw analog value (0 - 1023)
  rawValue = analogRead(fsrPin);

  // 2. Map it to a percentage (0 - 100) for easier VR scaling
  // We use 50 as a 'deadzone' so it stays at 0 when not touched
  mappedSpeed = map(rawValue, 50, 900, 0, 100);

  // 3. Keep it within 0-100 bounds
  if (mappedSpeed < 0)
    mappedSpeed = 0;
  if (mappedSpeed > 100)
    mappedSpeed = 100;

  // 4. Output for Serial Plotter
  Serial.print("Raw_Value:");
  Serial.print(rawValue);
  Serial.print(",");
  Serial.print("Broom_Speed:");
  Serial.println(mappedSpeed);

  delay(30); // Smooth refresh rate
}