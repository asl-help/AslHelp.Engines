//@if ==4.17
#pragma once

#include "CoreMinimal.h"
#include "Engine/GameViewportClient.h"
#include "FixtureViewportClient.generated.h"

// Draws nothing, because the game viewport of 4.17 clears a render target that the null RHI of the player check doesn't have
// and crashes the render thread.
UCLASS()
class UFixtureViewportClient : public UGameViewportClient
{
    GENERATED_BODY()

public:
    virtual void Draw(FViewport* InViewport, FCanvas* SceneCanvas) override;
};
//@endif
