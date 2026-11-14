// Provides the values the tests read. Sets them once when play begins and changes only Hp after that, so a test
// can assert them at any point of a run.
class FixtureData extends Actor;

// Defines a plain struct, so a reader has a nested value to resolve.
struct FixtureCoord
{
    var int X;
    var int Y;
};

enum EFixtureKind
{
    Wooden,
    Iron,
    Steel,
};

var int IntValue;
var float FloatValue;
var bool BoolValue;
var bool bFirst;
var bool bSecond;
var bool bThird;
var EFixtureKind Kind;
var string StringValue;
var name NameValue;
var FixtureCoord Coord;
var vector Position;
var array<int> IntArray;
var array<string> StringArray;
var array<FixtureCoord> CoordArray;
var FixtureItem Item;
var array<FixtureItem> Items;
var int Hp;

event PostBeginPlay()
{
    local FixtureItem Sword;
    local FixtureItem Shield;

    Super.PostBeginPlay();

    IntValue = 1337;
    FloatValue = 3.5;
    BoolValue = true;
    bFirst = true;
    bSecond = false;
    bThird = true;
    Kind = Iron;
    StringValue = "hello-fixture";
    NameValue = 'fixture-name';
    Coord.X = 7;
    Coord.Y = 9;
    Position.X = 1;
    Position.Y = 2;
    Position.Z = 3;
    IntArray.AddItem(100);
    IntArray.AddItem(200);
    IntArray.AddItem(300);
    StringArray.AddItem("a");
    StringArray.AddItem("b");
    StringArray.AddItem("c");
    CoordArray.Length = 2;
    CoordArray[0].X = 1;
    CoordArray[0].Y = 2;
    CoordArray[1].X = 3;
    CoordArray[1].Y = 4;

    Sword = new class'FixtureItem';
    Sword.ItemName = "sword";
    Sword.Count = 1;
    Shield = new class'FixtureItem';
    Shield.ItemName = "shield";
    Shield.Count = 3;
    Item = Sword;
    Items.AddItem(Sword);
    Items.AddItem(Shield);

    Hp = 1000;
    SetTimer(1.0, true);
}

// Lowers Hp by 5 a second, so a watcher has a value that changes.
event Timer()
{
    Hp -= 5;
}
