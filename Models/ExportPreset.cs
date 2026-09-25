using System;

namespace NihongoVocab.Models
{
    public class ExportPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = string.Empty;

        public string Pattern { get; set; } = @"^(.*?)\t?(.*?)\t?(.*)$";

        public string Replacement { get; set; } = "$1\t$2\t$3";

        public bool IsBuiltIn { get; set; } = false;

        public string Description { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
