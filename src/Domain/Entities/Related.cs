namespace DatabricksPoc.Domain.Entities;

public class Category
{
  public long CategoryId { get; set; }
  public string Name { get; set; } = default!;
  public string Slug { get; set; } = default!;

  public ICollection<Product> Products { get; set; } = [];
}

public class ProductTag
{
  public long ProductTagId { get; set; }
  public long ProductId { get; set; }
  public string Tag { get; set; } = default!;

  public Product Product { get; set; } = default!;
}
