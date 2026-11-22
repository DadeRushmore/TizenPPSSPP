#pragma once
#include "tizenpsp_jit_diagnostics.h"
// Include after the GLR command enums. Classify complete queue commands,
// including driver binding/setup inside DRAW, not individual GL function calls.
inline TizenProfilePhase TizenRenderProfile(GLRRenderCommand command) {
	switch (command) {
	case GLRRenderCommand::DRAW: return TizenProfilePhase::GLDraw;
	case GLRRenderCommand::UNIFORM4F:
	case GLRRenderCommand::UNIFORM4UI:
	case GLRRenderCommand::UNIFORM4I:
	case GLRRenderCommand::UNIFORMSTEREOMATRIX:
	case GLRRenderCommand::UNIFORMMATRIX: return TizenProfilePhase::GLUniform;
	case GLRRenderCommand::TEXTURE_SUBIMAGE: return TizenProfilePhase::GLUpload;
	default: return TizenProfilePhase::GLState;
	}
}
inline TizenProfilePhase TizenInitProfile(GLRInitStepType type) {
	switch (type) {
	case GLRInitStepType::CREATE_SHADER:
	case GLRInitStepType::CREATE_PROGRAM: return TizenProfilePhase::GLShader;
	case GLRInitStepType::BUFFER_SUBDATA:
	case GLRInitStepType::TEXTURE_IMAGE:
	case GLRInitStepType::TEXTURE_SUBIMAGE:
	case GLRInitStepType::TEXTURE_FINALIZE: return TizenProfilePhase::GLUpload;
	default: return TizenProfilePhase::GLState;
	}
}
