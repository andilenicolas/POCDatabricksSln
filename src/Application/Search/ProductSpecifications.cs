using System.Linq.Expressions;
using DatabricksPoc.Domain.Entities;
using DatabricksPoc.Domain.Models;

namespace DatabricksPoc.Application.Search;

// ─────────────────────────────────────────────────────────────────────────────
// Generic Specification — encapsulates a WHERE predicate as an expression tree.
// Linq2DB translates these to SQL at query time; no raw SQL involved.
// ─────────────────────────────────────────────────────────────────────────────

public interface ISpecification<T>
{
    Expression<Func<T, bool>> Criteria { get; }
}

public abstract class Specification<T> : ISpecification<T>
{
    public abstract Expression<Func<T, bool>> Criteria { get; }

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);
    public Specification<T> Or(Specification<T> other) => new OrSpecification<T>(this, other);
}

// ─────────────────────────────────────────────────────────────────────────────
// ParameterReplacer — rewrites one ParameterExpression to another inside an
// expression tree. Used by combinators to merge two lambdas into one flat
// AndAlso / OrElse without emitting any InvocationExpression nodes.
//
// Linq2DB translates plain AndAlso/OrElse expression trees natively.
// No LINQKit or any other expansion library is required.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ParameterReplacer(ParameterExpression from, Expression to)
    : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => node == from ? to : base.VisitParameter(node);
}

internal sealed class AndSpecification<T>(Specification<T> left, Specification<T> right)
    : Specification<T>
{
    public override Expression<Func<T, bool>> Criteria
    {
        get
        {
            var param = Expression.Parameter(typeof(T), "x");
            var leftBody = new ParameterReplacer(left.Criteria.Parameters[0], param).Visit(left.Criteria.Body);
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], param).Visit(right.Criteria.Body);
            return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(leftBody, rightBody), param);
        }
    }
}

internal sealed class OrSpecification<T>(Specification<T> left, Specification<T> right)
    : Specification<T>
{
    public override Expression<Func<T, bool>> Criteria
    {
        get
        {
            var param = Expression.Parameter(typeof(T), "x");
            var leftBody = new ParameterReplacer(left.Criteria.Parameters[0], param).Visit(left.Criteria.Body);
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], param).Visit(right.Criteria.Body);
            return Expression.Lambda<Func<T, bool>>(Expression.OrElse(leftBody, rightBody), param);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Product-specific specifications
// Each one is a small, independently testable Expression<Func<Product, bool>>.
// ─────────────────────────────────────────────────────────────────────────────

public class ProductNameContainsSpec(string term) : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.Name.Contains(term);
}

public class ProductBySkuSpec(string sku) : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.Sku == sku;
}

public class ProductByCategorySlugSpec(string slug) : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.Category.Slug == slug;
}

public class ProductPriceRangeSpec(decimal? min, decimal? max) : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => (!min.HasValue || p.Price >= min.Value)
          && (!max.HasValue || p.Price <= max.Value);
}

public class ProductInStockSpec() : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.Stock > 0;
}

public class ProductIsActiveSpec() : Specification<Product>
{
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.IsActive;
}

public class ProductHasTagsSpec(string[] tags) : Specification<Product>
{
    // EF Core translates this to: EXISTS (SELECT 1 FROM product_tags WHERE ...)
    public override Expression<Func<Product, bool>> Criteria =>
        p => p.Tags.Any(t => tags.Contains(t.Tag));
}

// ─────────────────────────────────────────────────────────────────────────────
// Builder — assembles specs from a search request into one combined predicate
// ─────────────────────────────────────────────────────────────────────────────

public static class ProductSpecificationBuilder
{
    public static Expression<Func<Product, bool>>? Build(ProductSearchRequest request)
    {
        Specification<Product>? combined = new ProductIsActiveSpec();

        if (!string.IsNullOrWhiteSpace(request.NameContains))
            combined = combined.And(new ProductNameContainsSpec(request.NameContains));

        if (!string.IsNullOrWhiteSpace(request.Sku))
            combined = combined.And(new ProductBySkuSpec(request.Sku));

        if (!string.IsNullOrWhiteSpace(request.CategorySlug))
            combined = combined.And(new ProductByCategorySlugSpec(request.CategorySlug));

        if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
            combined = combined.And(new ProductPriceRangeSpec(request.MinPrice, request.MaxPrice));

        if (request.InStockOnly == true)
            combined = combined.And(new ProductInStockSpec());

        if (request.Tags is { Length: > 0 })
            combined = combined.And(new ProductHasTagsSpec(request.Tags));

        return combined.Criteria;
    }
}
