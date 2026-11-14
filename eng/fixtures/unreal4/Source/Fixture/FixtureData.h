#pragma once

//@if >=4.15
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
//@else
#include "FixturePCH.h"
//@endif
#include "FixtureHero.h"
#include "FixtureItem.h"
#include "FixtureState.h"
#include "FixtureData.generated.h"

// Gives a reader a struct to resolve inside an object.
USTRUCT()
struct FFixtureCoord
{
    //@if >=4.8
    GENERATED_BODY()
    //@else
    GENERATED_USTRUCT_BODY()
    //@endif

    FFixtureCoord()
        : X(0)
        , Y(0)
    {
    }

    UPROPERTY()
    int32 X;

    UPROPERTY()
    int32 Y;
};

// Stores an enum class as a byte.
//@if >=4.5
UENUM()
enum class EFixtureKind : uint8
{
    Wooden,
    Iron,
    Steel,
};
//@else
UENUM()
namespace EFixtureKind
{
    enum Type
    {
        Wooden,
        Iron,
        Steel,
    };
}
//@endif

// Stores a plain enum through TEnumAsByte.
UENUM()
//@if >=5.1
enum EFixtureOldKind : int
//@else
enum EFixtureOldKind
//@endif
{
    OldWooden,
    OldIron,
};

// Keeps the known values. BeginPlay sets them once and only Hp changes after that.
UCLASS()
class AFixtureData : public AActor
{
    //@if >=4.6
    GENERATED_BODY()
    //@else
    GENERATED_UCLASS_BODY()
    //@endif

public:
    //@if >=4.7
    AFixtureData();

    //@endif
    //@if ==4.6
    AFixtureData(const FObjectInitializer& ObjectInitializer);

    //@endif
    UPROPERTY()
    int32 IntValue;

    UPROPERTY()
    int64 LongValue;

    UPROPERTY()
    float FloatValue;

    UPROPERTY()
    double DoubleValue;

    UPROPERTY()
    bool BoolValue;

    // Packs 3 bits into 1 byte, so a bool read has to use the field mask.
    UPROPERTY()
    uint8 bFirst : 1;

    UPROPERTY()
    uint8 bSecond : 1;

    UPROPERTY()
    uint8 bThird : 1;

    UPROPERTY()
    //@if >=4.5
    EFixtureKind Kind;
    //@else
    TEnumAsByte<EFixtureKind::Type> Kind;
    //@endif

    UPROPERTY()
    TEnumAsByte<EFixtureOldKind> OldKind;

    UPROPERTY()
    FString StringValue;

    UPROPERTY()
    FName NameValue;

    UPROPERTY()
    FText TextValue;

    UPROPERTY()
    FFixtureCoord Coord;

    UPROPERTY()
    FVector Position;

    UPROPERTY()
    TArray<int32> IntArray;

    UPROPERTY()
    TArray<FString> StringArray;

    UPROPERTY()
    TArray<FFixtureCoord> CoordArray;

    UPROPERTY()
    TArray<FVector> VecArray;

    //@if >=4.8
    UPROPERTY()
    TMap<FString, int32> Dict;

    UPROPERTY()
    TMap<int32, int32> IntDict;

    //@endif
    //@if >=4.13
    UPROPERTY()
    TSet<int32> IntSet;

    //@endif
    UPROPERTY()
    //@if >=5.0
    TObjectPtr<UFixtureItem> Item;
    //@else
    UFixtureItem* Item;
    //@endif

    UPROPERTY()
    //@if >=5.0
    TArray<TObjectPtr<UFixtureItem>> Items;
    //@else
    TArray<UFixtureItem*> Items;
    //@endif

    UPROPERTY()
    TWeakObjectPtr<UFixtureItem> WeakItem;

    UPROPERTY()
    //@if >=5.0
    TObjectPtr<UFixtureHero> Hero;
    //@else
    UFixtureHero* Hero;
    //@endif

    UPROPERTY()
    //@if >=5.0
    TObjectPtr<UFixtureState> CurrentState;
    //@else
    UFixtureState* CurrentState;
    //@endif

    // Drops by 5 a second, so a watcher sees a change.
    UPROPERTY()
    int32 Hp;

protected:
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;

private:
    float Elapsed;
};
