#pragma once

//@if >=4.15
#include "CoreMinimal.h"
#include "UObject/Object.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureEntity.generated.h"

// Starts a chain of 3 classes, so a read of Title goes through the fields of its base classes.
UCLASS()
class UFixtureEntity : public UObject
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    UPROPERTY()
    int32 Id;
};
