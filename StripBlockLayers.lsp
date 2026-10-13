;;; StripBlockLayers.lsp
;;; Command: STRIPBLK
;;;
;;; Puts every object inside the selected block definitions on layer 0 with
;;; Color, Linetype, Lineweight and Transparency set to ByLayer.
;;; - Nested blocks are handled too (recursively)
;;; - Dynamic blocks: both the master and the anonymous (*U) definitions
;;; - Attributes on the inserts are stripped as well
;;; - The block references themselves keep their own layer (e.g. A-GENM),
;;;   so the block contents now take on the insert's layer properties.
;;; - Press Enter at the selection prompt to strip ALL blocks in the drawing.
;;;   Xrefs and layouts are skipped.

(vl-load-com)

(defun stripblk:obj (obj)
  (foreach prop '((Layer . "0")
                  (Color . 256)              ; ByLayer
                  (Linetype . "ByLayer")
                  (Lineweight . -1)          ; ByLayer
                  (EntityTransparency . "ByLayer"))
    (if (vlax-property-available-p obj (car prop) T)
      (vl-catch-all-apply 'vlax-put-property (list obj (car prop) (cdr prop)))
    )
  )
)

(defun stripblk:atts (ref)
  (if (= :vlax-true (vla-get-hasattributes ref))
    (foreach att (vlax-invoke ref 'GetAttributes) (stripblk:obj att))
  )
)

(defun stripblk:def (name / blk)
  (if (and (not (member (strcase name) *stripblk-done*))
           (not (vl-catch-all-error-p
                  (setq blk (vl-catch-all-apply 'vla-item (list *stripblk-blks* name)))))
           (= :vlax-false (vla-get-isxref blk))
           (= :vlax-false (vla-get-islayout blk)))
    (progn
      (setq *stripblk-done* (cons (strcase name) *stripblk-done*))
      (vlax-for obj blk
        (stripblk:obj obj)
        (if (= (vla-get-objectname obj) "AcDbBlockReference")
          (stripblk:ref obj)
        )
      )
    )
  )
)

(defun stripblk:ref (ref)
  (stripblk:def (vla-get-name ref))
  (if (vlax-property-available-p ref 'EffectiveName)
    (stripblk:def (vla-get-effectivename ref))
  )
  (stripblk:atts ref)
)

(defun c:STRIPBLK (/ doc ss i n *stripblk-done* *stripblk-blks*)
  (setq doc             (vla-get-activedocument (vlax-get-acad-object))
        *stripblk-blks* (vla-get-blocks doc)
        n               0)
  (vla-startundomark doc)
  (princ "\nSelect blocks to strip <Enter for ALL blocks>: ")
  (if (setq ss (ssget '((0 . "INSERT"))))
    ;; selected blocks only
    (repeat (setq i (sslength ss))
      (stripblk:ref (vlax-ename->vla-object (ssname ss (setq i (1- i)))))
    )
    ;; all blocks in the drawing
    (progn
      (vlax-for blk *stripblk-blks* (stripblk:def (vla-get-name blk)))
      (if (setq ss (ssget "_X" '((0 . "INSERT") (66 . 1))))
        (repeat (setq i (sslength ss))
          (stripblk:atts (vlax-ename->vla-object (ssname ss (setq i (1- i)))))
        )
      )
    )
  )
  (setq n (length *stripblk-done*))
  (vla-regen doc acAllViewports)
  (vla-endundomark doc)
  (princ (strcat "\nStripped " (itoa n) " block definition(s) to layer 0 / ByLayer."))
  (princ)
)

(princ "\nStripBlockLayers loaded. Type STRIPBLK to run.")
(princ)
