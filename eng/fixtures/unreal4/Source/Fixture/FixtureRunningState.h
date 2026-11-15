#pragma once

//@if >=4.15
#include "CoreMinimal.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureState.h"
#include "FixtureRunningState.generated.h"

UCLASS()
class UFixtureRunningState : public UFixtureState
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    UPROPERTY()
    float Speed;
};
