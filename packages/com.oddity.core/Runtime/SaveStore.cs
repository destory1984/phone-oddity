using System;
using System.IO;

namespace Oddity.Core
{
    public class SaveStore
    {
        private readonly string _path;

        public SaveStore(string path)
        {
            _path = path;
        }

        /// <summary>Reads the save. Any read or parse failure gives a new game (recovered = true); a broken file is moved to .bad.</summary>
        public GameState Load(out bool recovered)
        {
            if (!File.Exists(_path))
            {
                recovered = false;
                return new GameState();
            }
            string json = null;
            bool readOk = false;
            try
            {
                json = File.ReadAllText(_path);
                readOk = true;
            }
            catch (Exception)
            {
            }
            if (readOk)
            {
                GameState s = GameState.FromJson(json, out recovered);
                if (!recovered) return s;
            }
            recovered = true;
            MoveAside();
            return new GameState();
        }

        private void MoveAside()
        {
            try
            {
                string bad = _path + ".bad";
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(_path, bad);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Writes the save. Never throws: false when it failed (the game goes on and tries again at the next save).</summary>
        public bool Save(GameState s)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, s.ToJson());
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>Message of the last failed Save, or null.</summary>
        public string LastError { get; private set; }
    }
}
