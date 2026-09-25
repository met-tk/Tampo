using System;

namespace NihongoVocab.Models
{
    public class CleansingPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = string.Empty;

        public string Pattern { get; set; } = string.Empty;

        public int CaptureGroupIndex { get; set; } = 1;

        public bool IsBuiltIn { get; set; } = false;

        public bool IsInverseFilter { get; set; } = false;

        public bool MatchWholeLine { get; set; } = false;

        public string Description { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
