using Raven.Client.Indexing;

namespace Raven.Client.Spatial
{
    public class SpatialCriteria
    {
        public SpatialRelation Relation { get; set; }
        public object Shape { get; set; }
        public double DistanceErrorPct { get; set; }
    }
}