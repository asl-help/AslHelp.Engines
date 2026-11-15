//@if <4.15
#include "FixturePCH.h"
//@endif
#include "FixtureData.h"
#include "FixtureRunningState.h"
//@if >=4.15
#include "HAL/PlatformProcess.h"
#include "Misc/FileHelper.h"
//@endif

//@if <4.6
UFixtureItem::UFixtureItem(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

UFixtureEntity::UFixtureEntity(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

UFixtureCharacter::UFixtureCharacter(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

UFixtureHero::UFixtureHero(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

UFixtureState::UFixtureState(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

UFixtureRunningState::UFixtureRunningState(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

//@endif
//@if >=4.7
AFixtureData::AFixtureData()
//@endif
//@if ==4.6
AFixtureData::AFixtureData(const FObjectInitializer& ObjectInitializer)
    : Super(ObjectInitializer)
//@endif
//@if <4.6
AFixtureData::AFixtureData(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
//@endif
{
    PrimaryActorTick.bCanEverTick = true;

    // Set defaults here, because engines before 4.5 refuse a member initializer on a reflected member.
    // Leave the other members at 0, because the engine zeroes object memory before the constructor runs.
    bFirst = 0;
    bSecond = 0;
    bThird = 0;
    Hp = 1000;
    Elapsed = 0.0f;
}

void AFixtureData::BeginPlay()
{
    Super::BeginPlay();

    IntValue = 1337;
    LongValue = 9000000000LL;
    FloatValue = 3.5f;
    DoubleValue = 2.718281828;
    BoolValue = true;
    bFirst = 1;
    bSecond = 0;
    bThird = 1;
    Kind = EFixtureKind::Iron;
    OldKind = OldIron;
    StringValue = TEXT("hello-fixture");
    NameValue = FName(TEXT("fixture-name"));
    TextValue = FText::FromString(TEXT("hello-text"));
    Coord.X = 7;
    Coord.Y = 9;
    Position = FVector(1.0f, 2.0f, 3.0f);
    IntArray.Add(100);
    IntArray.Add(200);
    IntArray.Add(300);
    StringArray.Add(TEXT("a"));
    StringArray.Add(TEXT("b"));
    StringArray.Add(TEXT("c"));
    CoordArray.SetNum(2);
    CoordArray[0].X = 1;
    CoordArray[0].Y = 2;
    CoordArray[1].X = 3;
    CoordArray[1].Y = 4;
    VecArray.Add(FVector(1.25f, -2.5f, 3.75f));
    VecArray.Add(FVector(4.5f, 5.25f, -6.75f));
    //@if >=4.8
    Dict.Add(TEXT("one"), 1);
    Dict.Add(TEXT("two"), 2);
    Dict.Add(TEXT("three"), 3);
    IntDict.Add(4, 40);
    IntDict.Add(5, 50);
    IntDict.Add(6, 60);
    //@endif
    //@if >=4.13
    IntSet.Add(11);
    IntSet.Add(22);
    IntSet.Add(33);
    //@endif

    Item = NewObject<UFixtureItem>(this);
    Item->ItemName = TEXT("sword");
    Item->Count = 1;

    UFixtureItem* Shield = NewObject<UFixtureItem>(this);
    Shield->ItemName = TEXT("shield");
    Shield->Count = 3;
    Items.Add(Item);
    Items.Add(Shield);
    WeakItem = Shield;

    Hero = NewObject<UFixtureHero>(this);
    Hero->Id = 7;
    Hero->Level = 12;
    Hero->Title = TEXT("the-brave");

    UFixtureRunningState* Running = NewObject<UFixtureRunningState>(this);
    Running->Speed = 8.5f;
    CurrentState = Running;

    Hp = 1000;

    // Write the marker last, so a file next to the executable means every value is set. Shipping doesn't have logs to watch.
    FFileHelper::SaveStringToFile(TEXT("ready"), *(FString(FPlatformProcess::BaseDir()) + TEXT("FixtureReady.txt")));
}

void AFixtureData::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);

    Elapsed += DeltaSeconds;

    if (Elapsed >= 1.0f)
    {
        Elapsed -= 1.0f;
        Hp -= 5;
    }
}
