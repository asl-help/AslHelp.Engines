//@if <4.15
#include "FixturePCH.h"
//@endif
#include "FixtureGameMode.h"
#include "Engine/World.h"

//@if ==4.6
AFixtureGameMode::AFixtureGameMode(const FObjectInitializer& ObjectInitializer)
    : Super(ObjectInitializer)
{
}

//@endif
//@if <4.6
AFixtureGameMode::AFixtureGameMode(const FPostConstructInitializeProperties& PCIP)
    : Super(PCIP)
{
}

//@endif
void AFixtureGameMode::BeginPlay()
{
    Super::BeginPlay();

    FixtureData = GetWorld()->SpawnActor<AFixtureData>();
}
