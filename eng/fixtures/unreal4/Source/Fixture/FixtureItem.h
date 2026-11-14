#pragma once

//@if >=4.15
#include "CoreMinimal.h"
#include "UObject/Object.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureItem.generated.h"

// Gives a reader an object pointer to follow.
UCLASS()
class UFixtureItem : public UObject
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    UPROPERTY()
    FString ItemName;

    UPROPERTY()
    int32 Count;
};
