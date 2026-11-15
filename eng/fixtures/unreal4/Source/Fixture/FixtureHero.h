#pragma once

//@if >=4.15
#include "CoreMinimal.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureCharacter.h"
#include "FixtureHero.generated.h"

UCLASS()
class UFixtureHero : public UFixtureCharacter
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    UPROPERTY()
    FString Title;
};
