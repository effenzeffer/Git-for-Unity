using System;
using System.Text.RegularExpressions;

namespace Unity.VersionControl.Git
{
    class LfsVersionOutputProcessor : BaseOutputProcessor<TheVersion>
    {
        public override void LineReceived(string line)
        {
            if (String.IsNullOrEmpty(line))
                return;

            var parts = line.Split('/', ' ');
            if (parts.Length > 1)
            {
                var version = TheVersion.Parse(parts[1]);
                RaiseOnEntry(version);
            }
        }
    }
}