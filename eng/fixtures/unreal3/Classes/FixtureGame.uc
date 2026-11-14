// Spawns the fixture actor when the map starts and keeps it, so a reader reaches it from the game.
class FixtureGame extends GameInfo;

var FixtureData FixtureData;

event PostBeginPlay()
{
    Super.PostBeginPlay();

    FixtureData = Spawn(class'FixtureData');

    // Log the ready line only when the spawn returned the fixture, so the build check passes only a player with the fixture.
    if (FixtureData != None)
    {
        `log("Fixture ready");
    }
}
