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

        public GameState Load(out bool recovered)
        {
            if (!File.Exists(_path))
            {
                recovered = false;
                return new GameState();
            }
            string json;
            try
            {
                json = File.ReadAllText(_path);
            }
            catch (IOException)
            {
                recovered = true;
                return new GameState();
            }
            return GameState.FromJson(json, out recovered);
        }

        public void Save(GameState s)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, s.ToJson());
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
    }
}
