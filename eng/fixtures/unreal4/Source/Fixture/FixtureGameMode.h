#pragma once

//@if >=4.15
#include "CoreMinimal.h"
//@else
#include "FixturePCH.h"
//@endif
//@if >=4.14
#include "GameFramework/GameModeBase.h"
//@else
#include "GameFramework/GameMode.h"
//@endif
#include "FixtureData.h"
#include "FixtureGameMode.generated.h"

// Spawns the data actor when the map starts and keeps it, so a reader reaches it from the game mode of the world.
UCLASS()
//@if >=4.14
class AFixtureGameMode : public AGameModeBase
//@else
class AFixtureGameMode : public AGameMode
//@endif
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    //@if ==4.6
    AFixtureGameMode(const FObjectInitializer& ObjectInitializer);

    //@endif
    UPROPERTY()
    //@if >=5.0
    TObjectPtr<AFixtureData> FixtureData;
    //@else
    AFixtureData* FixtureData;
    //@endif

protected:
    virtual void BeginPlay() override;
};
