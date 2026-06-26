namespace NetFileConverter
{
    /// <summary>
    /// Информация о компоненте для BOM и отчетов.
    /// </summary>
    public class ComponentInfo
    {
        public string Value { get; set; } = "~";
        public string Footprint { get; set; } = "~";
        public bool IsMissingFp { get; set; }
    }
}
