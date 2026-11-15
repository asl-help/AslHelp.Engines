// Completes the types that the plugins and the library read. The engine libs don't have debug types,
// so a type only shows up in the PDB when a compile unit of the fixture completes and uses it.
//
// FNamePool can't be here, because its definition sits in UnrealNames.cpp.
// The text data behind FText can't be here either, because its class sits in a private file of Core.

//@if <4.15
#include "FixturePCH.h"
//@else
#include "CoreMinimal.h"
#include "Containers/BitArray.h"
#include "Containers/Map.h"
#include "Containers/ScriptArray.h"
#include "Containers/Set.h"
#include "Containers/SparseArray.h"
#include "UObject/Class.h"
#include "UObject/EnumProperty.h"
#include "UObject/NameTypes.h"
#include "UObject/Object.h"
#include "UObject/Package.h"
#include "UObject/UnrealType.h"
#include "UObject/UObjectArray.h"
#include "UObject/WeakObjectPtr.h"
//@endif
//@if >=4.25
#include "UObject/Field.h"
//@endif
#include "Engine/DataTable.h"
#include "Engine/Engine.h"
#include "Engine/GameEngine.h"
//@if >=4.4
#include "Engine/GameInstance.h"
//@endif
#include "Engine/GameViewportClient.h"
#include "Engine/LocalPlayer.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/GameMode.h"
#include "GameFramework/GameState.h"
//@if >=4.14
#include "GameFramework/GameModeBase.h"
#include "GameFramework/GameStateBase.h"
//@endif
#include "GameFramework/MovementComponent.h"
#include "GameFramework/PlayerState.h"
#include "GameFramework/WorldSettings.h"
//@if >=4.22
#include "Subsystems/SubsystemCollection.h"
//@endif

// Fails the compile when the type isn't complete and keeps a pointer to it in a volatile local,
// because the optimizer drops any other unused local and its type record goes with it.
#define FIXTURE_KEEP_TYPE(Type) \
    { \
        Type* volatile Kept = nullptr; \
        (void)Kept; \
        (void)sizeof(Type); \
    }

FIXTURE_API void FixtureKeepTypes();

void FixtureKeepTypes()
{
    FIXTURE_KEEP_TYPE(UObjectBase)
    FIXTURE_KEEP_TYPE(UObject)
    FIXTURE_KEEP_TYPE(UField)
    FIXTURE_KEEP_TYPE(UStruct)
    FIXTURE_KEEP_TYPE(UClass)
    FIXTURE_KEEP_TYPE(UEnum)
    FIXTURE_KEEP_TYPE(UPackage)

    //@if >=4.25
    FIXTURE_KEEP_TYPE(FField)
    FIXTURE_KEEP_TYPE(FFieldClass)
    FIXTURE_KEEP_TYPE(FProperty)
    FIXTURE_KEEP_TYPE(FBoolProperty)
    FIXTURE_KEEP_TYPE(FObjectProperty)
    FIXTURE_KEEP_TYPE(FStructProperty)
    FIXTURE_KEEP_TYPE(FArrayProperty)
    FIXTURE_KEEP_TYPE(FMapProperty)
    FIXTURE_KEEP_TYPE(FSetProperty)
    FIXTURE_KEEP_TYPE(FByteProperty)
    FIXTURE_KEEP_TYPE(FEnumProperty)
    //@else
    FIXTURE_KEEP_TYPE(UProperty)
    FIXTURE_KEEP_TYPE(UBoolProperty)
    FIXTURE_KEEP_TYPE(UObjectProperty)
    FIXTURE_KEEP_TYPE(UStructProperty)
    FIXTURE_KEEP_TYPE(UArrayProperty)
    FIXTURE_KEEP_TYPE(UByteProperty)
    //@if >=4.8
    FIXTURE_KEEP_TYPE(UMapProperty)
    //@endif
    //@if >=4.13
    FIXTURE_KEEP_TYPE(USetProperty)
    //@endif
    //@if >=4.15
    FIXTURE_KEEP_TYPE(UEnumProperty)
    //@endif
    //@endif

    FIXTURE_KEEP_TYPE(FName)
    FIXTURE_KEEP_TYPE(FNameEntry)
    FIXTURE_KEEP_TYPE(FString)
    FIXTURE_KEEP_TYPE(FScriptArray)
    FIXTURE_KEEP_TYPE(FWeakObjectPtr)
    //@if >=4.8
    FIXTURE_KEEP_TYPE(FScriptBitArray)
    FIXTURE_KEEP_TYPE(FScriptSparseArray)
    FIXTURE_KEEP_TYPE(FScriptSet)
    FIXTURE_KEEP_TYPE(FScriptSetLayout)
    FIXTURE_KEEP_TYPE(FScriptMap)
    FIXTURE_KEEP_TYPE(FScriptMapLayout)
    //@endif

    FIXTURE_KEEP_TYPE(FUObjectArray)
    //@if >=4.11
    FIXTURE_KEEP_TYPE(FUObjectItem)
    //@if <4.20
    FIXTURE_KEEP_TYPE(FFixedUObjectArray)
    //@endif
    //@endif
    //@if >=4.20
    FIXTURE_KEEP_TYPE(FChunkedFixedUObjectArray)
    //@endif

    FIXTURE_KEEP_TYPE(UEngine)
    FIXTURE_KEEP_TYPE(UGameEngine)
    FIXTURE_KEEP_TYPE(ULocalPlayer)
    FIXTURE_KEEP_TYPE(UGameViewportClient)
    FIXTURE_KEEP_TYPE(UWorld)
    FIXTURE_KEEP_TYPE(FWorldContext)
    FIXTURE_KEEP_TYPE(AWorldSettings)
    FIXTURE_KEEP_TYPE(AGameMode)
    FIXTURE_KEEP_TYPE(AGameState)
    //@if >=4.14
    FIXTURE_KEEP_TYPE(AGameModeBase)
    FIXTURE_KEEP_TYPE(AGameStateBase)
    //@endif
    FIXTURE_KEEP_TYPE(ACharacter)
    FIXTURE_KEEP_TYPE(APlayerState)
    FIXTURE_KEEP_TYPE(UMovementComponent)
    FIXTURE_KEEP_TYPE(UCharacterMovementComponent)
    FIXTURE_KEEP_TYPE(UDataTable)
    //@if >=4.4
    FIXTURE_KEEP_TYPE(UGameInstance)
    //@endif
    //@if >=4.22
    FIXTURE_KEEP_TYPE(FSubsystemCollectionBase)
    //@endif
}
