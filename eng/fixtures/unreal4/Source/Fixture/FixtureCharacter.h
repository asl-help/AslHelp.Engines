#pragma once

//@if >=4.15
#include "CoreMinimal.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureEntity.h"
#include "FixtureCharacter.generated.h"

UCLASS()
class UFixtureCharacter : public UFixtureEntity
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    UPROPERTY()
    int32 Level;
};
