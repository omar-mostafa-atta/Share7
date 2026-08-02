namespace Share7.Domain.LookUps;

public class Grade
{
    public Guid Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public int Order { get; set; }
}
