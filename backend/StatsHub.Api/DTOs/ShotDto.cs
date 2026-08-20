namespace StatsHub.Api.DTOs
{
    public class ShotDto
    {
        public int Id { get; set; }
        public int GameStatsId { get; set; }
        public int GameId { get; set; }
        public int PlayerId { get; set; }
        public int Quarter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Made { get; set; }
        public int Value { get; set; }
    }

    public class CreateShotDto
    {
        public int GameStatsId { get; set; }
        public int Quarter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Made { get; set; }
        public int Value { get; set; }
    }
}
