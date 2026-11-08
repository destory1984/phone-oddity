namespace Oddity.RoomTest
{
    // Shift+1 .. Shift+0 hold one of the model's custom VRM expressions; the same key again, or 0, clears it
    public static class FaceKeys
    {
        public static readonly string[] Names = { "eyeSmile", "happyClosed", "bigSmile", "mouthSmile", "mouthOpen", "browRaise", "browSoft", "browSad", "browAngry" };
        // the face held after digit 0-9 is pressed with Shift while `held` was on: null = the normal face
        public static string Press(int digit, string held)
        {
            if (digit < 1 || digit > Names.Length) return null;
            string name = Names[digit - 1];
            return name == held ? null : name;
        }
        // script key "face0".."face9" -> digit, or -1
        public static int ScriptDigit(string key) => key != null && key.Length == 5 && key.StartsWith("face") && char.IsDigit(key[4]) ? key[4] - '0' : -1;
    }
}
