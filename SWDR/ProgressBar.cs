using System;

namespace SWDR
{
    public class ProgressBar
    {
        private int _lastOutputLength;
        private readonly int _maximumWidth;

        public ProgressBar(int maximumWidth)
        {
            _maximumWidth = maximumWidth;
            Console.Write(" [ ");
        }

        public void Update(double percentage)
        {
            Console.Write(string.Empty.PadRight(_lastOutputLength, '\b'));

            int width = (int)(percentage / 100 * _maximumWidth);
            int emptyFill = _maximumWidth - width;

            string output = $"{string.Empty.PadLeft(width, '=')}{string.Empty.PadLeft(emptyFill, ' ')} ] {percentage.ToString("0.0")}%";
            Console.Write(output);

            _lastOutputLength = output.Length;
        }
    }
}
