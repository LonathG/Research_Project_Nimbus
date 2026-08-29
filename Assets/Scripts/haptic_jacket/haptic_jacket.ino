const int hapticPin1 = 3;
const int hapticPin2 = 5;
int currentIntensity = 0;

void setup() {
  Serial.begin(115200);
  pinMode(hapticPin1, OUTPUT);
  pinMode(hapticPin2, OUTPUT);
  Serial.println("Haptic Jacket Ready (Pins 3 and 5).");
  Serial.println("Debug Keys: 'w' = forward max, 's' = stop, '+' = increase intensity, '-' = decrease intensity");
}

void loop() {
  if (Serial.available() > 0) {
    String command = Serial.readStringUntil('\n');
    command.trim(); // Remove whitespace/CRLF

    if (command.startsWith("MOVE_FWD:")) {
      // Command from Unity: MOVE_FWD:128
      String valueStr = command.substring(9);
      currentIntensity = valueStr.toInt();
      currentIntensity = constrain(currentIntensity, 0, 255);
      Serial.print("Unity Command - Forward Intensity: ");
      Serial.println(currentIntensity);
    } 
    else if (command.length() == 1) {
      // Debug keys from Serial Monitor
      char key = command.charAt(0);
      if (key == 'w' || key == 'W') {
        currentIntensity = 255;
        Serial.println("Debug: Forward (Max Intensity)");
      }
      else if (key == 's' || key == 'S') {
        currentIntensity = 0;
        Serial.println("Debug: Stop");
      }
      else if (key == '+') {
        currentIntensity = constrain(currentIntensity + 25, 0, 255);
        Serial.print("Debug: Intensity Increased to ");
        Serial.println(currentIntensity);
      }
      else if (key == '-') {
        currentIntensity = constrain(currentIntensity - 25, 0, 255);
        Serial.print("Debug: Intensity Decreased to ");
        Serial.println(currentIntensity);
      }
      else {
        Serial.println("Unknown debug key.");
      }
    }
    
    // Apply the intensity to both haptic motors using PWM
    analogWrite(hapticPin1, currentIntensity);
    analogWrite(hapticPin2, currentIntensity);
  }
}