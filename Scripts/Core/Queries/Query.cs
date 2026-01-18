using Core.Systems;
using Core.Units;

namespace Core.Queries
{
    /// <summary>
    /// Static entry point for fluent query builders.
    ///
    /// Usage:
    ///   using var provinces = Query.Provinces(gameState)
    ///       .OwnedBy(countryId)
    ///       .IsLand()
    ///       .Execute(Allocator.Temp);
    ///
    ///   using var countries = Query.Countries(gameState)
    ///       .WithMinProvinces(5)
    ///       .Execute(Allocator.Temp);
    ///
    ///   using var units = Query.Units(unitSystem)
    ///       .OwnedBy(countryId)
    ///       .InProvince(provinceId)
    ///       .Execute(Allocator.Temp);
    /// </summary>
    public static class Query
    {
        /// <summary>
        /// Create a province query builder.
        /// </summary>
        public static ProvinceQueryBuilder Provinces(GameState gameState)
        {
            return new ProvinceQueryBuilder(gameState.Provinces, gameState.Adjacencies);
        }

        /// <summary>
        /// Create a province query builder with explicit systems.
        /// </summary>
        public static ProvinceQueryBuilder Provinces(ProvinceSystem provinceSystem, AdjacencySystem adjacencySystem = null)
        {
            return new ProvinceQueryBuilder(provinceSystem, adjacencySystem);
        }

        /// <summary>
        /// Create a country query builder.
        /// </summary>
        public static CountryQueryBuilder Countries(GameState gameState)
        {
            return new CountryQueryBuilder(gameState.Countries, gameState.Provinces, gameState.Adjacencies);
        }

        /// <summary>
        /// Create a country query builder with explicit systems.
        /// </summary>
        public static CountryQueryBuilder Countries(CountrySystem countrySystem, ProvinceSystem provinceSystem = null, AdjacencySystem adjacencySystem = null)
        {
            return new CountryQueryBuilder(countrySystem, provinceSystem, adjacencySystem);
        }

        /// <summary>
        /// Create a unit query builder.
        /// </summary>
        public static UnitQueryBuilder Units(UnitSystem unitSystem)
        {
            return new UnitQueryBuilder(unitSystem);
        }
    }
}
