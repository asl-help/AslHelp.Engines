//@if >=4.22
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "FixtureSubsystem.generated.h"

// Gives a reader a value to reach through the game instance and its subsystem collection.
UCLASS()
class UFixtureSubsystem : public UGameInstanceSubsystem
{
    GENERATED_BODY()

public:
    UPROPERTY()
    int32 SubsystemValue;

    virtual void Initialize(FSubsystemCollectionBase& Collection) override;
};
//@endif
