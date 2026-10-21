from ..contracts import EvalCase

def case(id,suite,handler,description,level='unit',category='Validation',**kwargs):
    return EvalCase(id,suite,description,handler,inputs={'fixture':'synthetic_contract' if level=='unit' else 'isolated_fixture'},expected={'assertions':'all documented runtime assertions must hold'},level=level,category=category,**kwargs)

CASES=[
    case('state.readonly','state','readonly','Nested immutable snapshot cannot be mutated',category='State'),
    case('state.general','state','general','General context includes no fabricated exercise facts',category='State'),
    case('state.cancelled_history','state','history','Actual cancellation registry excludes pending assistant history',category='History'),
    case('state.malformed','state','malformed','Unknown enum/version/identity/size rejects state only',category='State'),
    case('state.budget','state','budget','Drop oldest whole pairs; preserve persona/JSON and character budget',category='State'),
    case('conversation.valid','conversation','valid','Validate actual Schema V2 normal fixture'),
    case('conversation.speech_only','conversation','speech_only','Speech-only leaves every body channel none'),
    case('conversation.invalid_json','conversation','rescue_json','Actual primary/rescue path handles malformed JSON safely',category='LLM'),
    case('conversation.invalid_enum','conversation','enum','Unsupported enum is neutralized by existing validator'),
    case('conversation.context_present','conversation','context','Captured provider messages contain observed Lead state',category='State'),
    case('conversation.streaming_chunks','conversation','chunks','Actual incremental parser handles fixed UTF8/escaped chunks',category='LLM'),
    case('reliability.history_once','reliability','history_once','Successful commit exactly once; cancellation commits zero',category='History'),
    case('reliability.late_result','reliability','late','Cancelled actual registry rejects late results',category='Cancellation'),
    case('performance.context','performance','context_cost','Measure actual ContextBuilder duration and size',category='Performance'),
    case('performance.thresholds','performance','thresholds','Configurable WARN/FAIL comparator is deterministic',category='Performance'),
    case('reliability.exit_codes','reliability','exit_codes','Required/optional FAIL and configured SKIP/WARN produce correct exit codes',category='Infrastructure'),
    case('state.http_malformed','state','http_malformed','Actual Agent endpoint keeps responding after rejected state',level='integration',category='State'),
    case('conversation.http_stream','conversation','http_stream','Actual streaming endpoint emits valid fixture final',level='integration',category='LLM'),
    case('reliability.http_history','reliability','http_history','HTTP pending/cancel/complete safety with actual registry',level='integration',category='History'),
]

UNITY=[
 ('state.idle_source','state','Idle snapshot matches actual sources'),
 ('state.speaking_source','state','Speaking snapshot matches actual SpeechArbiter'),
 ('state.lead_source','state','Effective Lead snapshot matches actual Follow'),
 ('state.general_source','state','General mode snapshot omits fake exercise fields'),
 ('speech.normal','speech','Prepare鈫扨lay鈫扖omplete runtime order'),
 ('speech.queue','speech','Two speech requests FIFO without overlap'),
 ('speech.barge_cancel','speech','Active stop, queue clear and mouth closure'),
 ('speech.streaming','speech','PCM/buffer contract without normal underrun'),
 ('speech.source3d','speech','Single official 3D TTS source and one active listener'),
 ('speech.lipsync','speech','Mouth activates and closes on completion/cancellation'),
 ('behavior.nod','behavior','Adapter鈫扥rchestrator accepts Nod'),
 ('behavior.wave','behavior','Adapter鈫扥rchestrator accepts Wave'),
 ('behavior.expression','behavior','Adapter semantic happy mapping reaches expression controller'),
 ('behavior.lead','behavior','Adapter spatial Lead is accepted'),
 ('behavior.speech_only','behavior','Speech-only dispatches no body action'),
 ('behavior.user_speaking','behavior','Pending gesture cleared when user takes floor'),
 ('behavior.cooldown','behavior','Repeated gesture is blocked as Busy/Cooldown'),
 ('reliability.llm_before','reliability','Stream unavailable before speech uses complete Agent'),
 ('reliability.tts_before','reliability','Stream unavailable before playback uses complete TTS'),
 ('reliability.llm_after','reliability','After-partial LLM failure does not replay/commit'),
 ('reliability.tts_after','reliability','After-partial PCM failure stops without replay/commit'),
 ('reliability.cancel_cleanup','reliability','Cancellation leaves no stale playback'),
 ('performance.unity','performance','Aggregate measured Unity trace without fabricated hardware metrics'),
 ('reliability.clean_scene','reliability','Play exits clean; original Scene not saved or modified'),
]
for id,suite,description in UNITY:
    CASES.append(case(id,suite,'unity',description,level='integration',category={'state':'State','speech':'Playback','behavior':'Behavior','performance':'Performance','reliability':'Cancellation'}[suite],timeout=180,tags=('unity_runtime','synthetic_audio')))
CASES.append(case('conversation.deepseek_live','conversation','live_llm','Optional real DeepSeek validates schema, not exact reply wording',level='live',category='LLM',required=False,timeout=90,tags=('provider_live',)))
CASES.append(case('speech.roxy_live','speech','unity','Optional retained physical WAV鈫抮eal ASR/DeepSeek/Roxy/Avatar playback',level='live',category='Playback',required=False,timeout=150,tags=('provider_live','local_only_audio')))
CASES.append(case('performance.physical_baseline','performance','physical_metrics','Compare a fresh comparable physical run; absence is SKIP',level='live',category='Performance',required=False,tags=('physical_manual',)))

from .tool_catalog import CASES as TOOL_CASES
CASES.extend(TOOL_CASES)

from .native_catalog import CASES as NATIVE_CASES
CASES.extend(NATIVE_CASES)

# Unified reliability: deterministic and actual-module isolated fixtures.
CASES.append(case('reliability.policy_llm_pre','reliability','rel_llm_pre','Unified reliability llm_pre',category='Infrastructure'))
CASES.append(case('reliability.policy_llm_post','reliability','rel_llm_post','Unified reliability llm_post',category='Infrastructure'))
CASES.append(case('reliability.policy_tts_pre','reliability','rel_tts_pre','Unified reliability tts_pre',category='Infrastructure'))
CASES.append(case('reliability.policy_tts_post','reliability','rel_tts_post','Unified reliability tts_post',category='Infrastructure'))
CASES.append(case('reliability.policy_normal_rejection','reliability','rel_normal_rejection','Unified reliability normal_rejection',category='Infrastructure'))
CASES.append(case('reliability.policy_tool_timeout','reliability','rel_tool_timeout','Unified reliability tool_timeout',category='Infrastructure'))
CASES.append(case('reliability.policy_native_post','reliability','rel_native_post','Unified reliability native_post',category='Infrastructure'))
CASES.append(case('reliability.policy_cancel_priority','reliability','rel_cancel_priority','Unified reliability cancel_priority',category='Infrastructure'))
CASES.append(case('reliability.policy_loop','reliability','rel_loop','Unified reliability loop',category='Infrastructure'))
CASES.append(case('reliability.policy_depth','reliability','rel_depth','Unified reliability depth',category='Infrastructure'))
CASES.append(case('reliability.policy_fallback_failed','reliability','rel_fallback_failed','Unified reliability fallback_failed',category='Infrastructure'))
CASES.append(case('reliability.policy_retry_budget','reliability','rel_retry_budget','Unified reliability retry_budget',category='Infrastructure'))
CASES.append(case('reliability.policy_invalid_state','reliability','rel_invalid_state','Unified reliability invalid_state',category='Infrastructure'))
CASES.append(case('reliability.policy_actual_timeout','reliability','rel_actual_timeout','Unified reliability actual_timeout',category='Infrastructure'))
CASES.append(case('reliability.policy_history_once','reliability','rel_history_once','Unified reliability history_once',category='Infrastructure'))
CASES.append(case('reliability.policy_history_cancel','reliability','rel_history_cancel','Unified reliability history_cancel',category='Infrastructure'))
CASES.append(case('reliability.policy_cancel_race','reliability','rel_cancel_race','Unified reliability cancel_race',category='Infrastructure'))
CASES.append(case('reliability.policy_health','reliability','rel_health','Unified reliability health',category='Infrastructure'))
CASES.append(case('reliability.policy_safe_record','reliability','rel_safe_record','Unified reliability safe_record',category='Infrastructure'))
CASES.append(case('reliability.policy_known_issue','reliability','rel_known_issue','Unified reliability known_issue',category='Infrastructure'))
CASES.append(case('reliability.policy_overhead','reliability','rel_overhead','Unified reliability overhead',category='Infrastructure'))
CASES.append(case('reliability.policy_edge_router','reliability','rel_edge_router','Unified reliability edge_router',category='Infrastructure'))

CASES.append(case('reliability.policy_actual_rescue_failure','reliability','rel_actual_rescue_failure','Existing modified-input rescue terminates after one attempt',category='LLM'))
CASES.append(case('reliability.policy_edge_cancel','reliability','rel_edge_cancel','Provider cancellation never reaches Edge',category='Cancellation'))
CASES.append(case('reliability.fallback_chain_failed','reliability','unity','Actual Unity fallback_chain_failed',level='integration',category='Infrastructure',timeout=180,tags=('unity_runtime','synthetic_audio')))
CASES.append(case('reliability.cancel_fallback_race','reliability','unity','Actual Unity cancel_fallback_race',level='integration',category='Infrastructure',timeout=180,tags=('unity_runtime','synthetic_audio')))
CASES.append(case('reliability.invalid_state_speech','reliability','unity','Actual Unity invalid_state_speech',level='integration',category='Infrastructure',timeout=180,tags=('unity_runtime','synthetic_audio')))

# Required performance measurement contracts (not hardware threshold assertions).
CASES.append(case('performance.measurement_integrity','performance','perf_integrity','Performance profiling contract: measurement_integrity',category='Performance'))
CASES.append(case('performance.no_fake_metrics','performance','perf_no_fake','Performance profiling contract: no_fake_metrics',category='Performance'))
CASES.append(case('performance.budget_schema','performance','perf_budget','Performance profiling contract: budget_schema',category='Performance'))
CASES.append(case('performance.baseline_serialization','performance','perf_serialization','Performance profiling contract: baseline_serialization',category='Performance'))

CASES.append(case('asr.baseline_quality','asr','asr_baseline_quality','Measured medium runtime: baseline_quality',category='Performance'))

CASES.append(case('asr.candidate_quality','asr','asr_candidate_quality','Measured medium runtime: candidate_quality',category='Performance'))

CASES.append(case('asr.warm_latency','asr','asr_warm_latency','Measured medium runtime: warm_latency',category='Performance'))

CASES.append(case('asr.model_resident','asr','asr_model_resident','Measured medium runtime: model_resident',category='Performance'))

CASES.append(case('asr.readiness','asr','asr_readiness','Measured medium runtime: readiness',category='Performance'))

CASES.append(case('tts.ready_state','tts_runtime','tts_runtime_ready_state','Measured Roxy runtime: ready_state',category='Performance'))

CASES.append(case('tts.first_pcm_cold','tts_runtime','tts_runtime_first_pcm_cold','Measured Roxy runtime: first_pcm_cold',category='Performance'))

CASES.append(case('tts.first_pcm_warm','tts_runtime','tts_runtime_first_pcm_warm','Measured Roxy runtime: first_pcm_warm',category='Performance'))

CASES.append(case('tts.true_underrun','tts_runtime','tts_runtime_true_underrun','Measured Roxy runtime: true_underrun',category='Performance'))

CASES.append(case('tts.buffer_depth','tts_runtime','tts_runtime_buffer_depth','Measured Roxy runtime: buffer_depth',category='Performance'))

CASES.append(case('tts.barge_cancel','tts_runtime','tts_runtime_barge_cancel','Measured Roxy runtime: barge_cancel',category='Performance'))

# Physical platform gates, explicitly opt-in hardware level. Never substitutes Editor data.
CASES.append(case('quest.xr_initialization','quest','quest_xr_initialization','Physical Quest xr_initialization',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.camera_tracking','quest','quest_camera_tracking','Physical Quest camera_tracking',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.controller_input','quest','quest_controller_input','Physical Quest controller_input',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.microphone','quest','quest_microphone','Physical Quest microphone',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.backend_health','quest','quest_backend_health','Physical Quest backend_health',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.streaming_llm','quest','quest_streaming_llm','Physical Quest streaming_llm',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.streaming_tts','quest','quest_streaming_tts','Physical Quest streaming_tts',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.tool_result','quest','quest_tool_result','Physical Quest tool_result',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.spatial_voice','quest','quest_spatial_voice','Physical Quest spatial_voice',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
CASES.append(case('quest.performance','quest','quest_performance','Physical Quest performance',level='hardware',category='Infrastructure',tags=('quest_hardware',)))
