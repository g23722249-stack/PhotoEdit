"""產生測試用的極小 ONNX 去背模型（不是真的 AI 模型，只模擬輸入輸出介面）。

輸出 = 通道平均 × 8（亮的像素 → 正值 → 前景）。
- tiny_sigmoid.onnx：輸出已套 sigmoid（機率，0..1）
- tiny_logits.onnx ：輸出是 logits（需要由程式套 sigmoid）
- tiny_fp16.onnx   ：輸入輸出為 float16，內部轉 float32 計算
用法：python make_test_models.py <輸出資料夾>
"""
import sys, os
import onnx
from onnx import helper, TensorProto

S = 64

def build(name, sigmoid=True, fp16=False):
    io_type = TensorProto.FLOAT16 if fp16 else TensorProto.FLOAT
    nodes, inits = [], []
    x = "input"
    if fp16:
        nodes.append(helper.make_node("Cast", [x], ["x32"], to=TensorProto.FLOAT)); x = "x32"
    inits.append(helper.make_tensor("axes", TensorProto.INT64, [1], [1]))
    inits.append(helper.make_tensor("k", TensorProto.FLOAT, [], [8.0]))
    nodes.append(helper.make_node("ReduceMean", [x, "axes"], ["m"], keepdims=1))
    nodes.append(helper.make_node("Mul", ["m", "k"], ["logit"]))
    y = "logit"
    if sigmoid:
        nodes.append(helper.make_node("Sigmoid", [y], ["prob"])); y = "prob"
    if fp16:
        nodes.append(helper.make_node("Cast", [y], ["output"], to=TensorProto.FLOAT16))
    else:
        nodes.append(helper.make_node("Identity", [y], ["output"]))
    g = helper.make_graph(nodes, name,
        [helper.make_tensor_value_info("input", io_type, [1, 3, S, S])],
        [helper.make_tensor_value_info("output", io_type, [1, 1, S, S])], inits)
    m = helper.make_model(g, opset_imports=[helper.make_opsetid("", 18)], producer_name="autocomposite-tests")
    m.ir_version = 8
    onnx.checker.check_model(m)
    return m

out = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(out, exist_ok=True)
onnx.save(build("tiny_sigmoid"), os.path.join(out, "tiny_sigmoid.onnx"))
onnx.save(build("tiny_logits", sigmoid=False), os.path.join(out, "tiny_logits.onnx"))
onnx.save(build("tiny_fp16", fp16=True), os.path.join(out, "tiny_fp16.onnx"))
print("ok")
