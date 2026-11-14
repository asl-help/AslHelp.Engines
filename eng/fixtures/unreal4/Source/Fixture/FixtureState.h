#pragma once

//@if >=4.15
#include "CoreMinimal.h"
#include "UObject/Object.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureState.generated.h"

// Sits behind a pointer of this type while the object is a UFixtureRunningState, so a read has to use the class of the object.
UCLASS()
class UFixtureState : public UObject
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif
};
