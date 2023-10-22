namespace FLEX_0S9_Net
{
    internal class PortParameters
    {
        public int Num { get; set; }
        public string Device { get; set; }
        public int Rate { get; set; }
        public bool Verbose { get; set; }
        public bool AutoMount { get; set; }
        public bool AutoShutdown { get; set; }
        public string DefaultDirectory {  get; set; }
        public string[] ImageFiles {  get; set; }
    }
}
