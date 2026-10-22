;;; BlocksToLayer0.lsp
;;; ---------------------------------------------------------------------------
;;; Command: BLK0
;;; Alias:   BLOCKSTOLAYER0
;;;
;;; For every object inside every block definition in the drawing (including
;;; nested and dynamic/anonymous blocks):
;;;   - Layer        -> 0
;;;   - Color        -> ByLayer
;;;   - Linetype     -> ByLayer
;;;   - Lineweight   -> ByLayer
;;;   - Transparency -> ByLayer (AutoCAD 2011 and later)
;;;
;;; Attributes on block references are changed the same way, wherever the
;;; reference sits. The block references themselves are not moved off their
;;; layers, and objects drawn directly in Model/Paper Space are left alone.
;;;
;;; Xrefs and xref-dependent blocks are skipped (they can't be edited here).
;;; Locked layers are temporarily unlocked so their objects can be changed,
;;; then re-locked. The whole operation is a single UNDO step.
;;;
;;; Usage: APPLOAD this file (or drag it into the drawing), then type BLK0.
;;; ---------------------------------------------------------------------------

(vl-load-com)

;; Put one object on layer 0 with ByLayer properties. Returns T on success.
(defun blk0:fix (obj / ok)
  (setq ok T)
  (foreach call (list (list 'vla-put-Layer      obj "0")
                      (list 'vla-put-Color      obj acByLayer)
                      (list 'vla-put-Linetype   obj "ByLayer")
                      (list 'vla-put-Lineweight obj acLnWtByLayer))
    (if (vl-catch-all-error-p (vl-catch-all-apply (car call) (cdr call)))
      (setq ok nil)))
  ;; Transparency doesn't exist before AutoCAD 2011, so its failure is ignored.
  (vl-catch-all-apply 'vla-put-EntityTransparency (list obj "ByLayer"))
  ok)

(defun c:BLK0 (/ acad doc layers locked count failed inLayout)
  (setq acad   (vlax-get-acad-object)
        doc    (vla-get-ActiveDocument acad)
        layers (vla-get-Layers doc)
        locked '()
        count  0
        failed 0)

  (vla-StartUndoMark doc)

  ;; Unlock any locked layers, remembering which ones to re-lock.
  (vlax-for lay layers
    (if (= (vla-get-Lock lay) :vlax-true)
      (progn
        (setq locked (cons lay locked))
        (vla-put-Lock lay :vlax-false))))

  (vlax-for blk (vla-get-Blocks doc)
    (if (and (= (vla-get-IsXref blk) :vlax-false)
             ;; Skip xref-dependent blocks and the internal anonymous blocks
             ;; that dimensions (*D) and tables (*T) are drawn from.
             (not (wcmatch (vla-get-Name blk) "*|*,`*D*,`*T*")))
      (progn
        ;; Model Space and Paper Space layouts are also entries in the Blocks
        ;; collection; there only attributes on block references are changed.
        (setq inLayout (= (vla-get-IsLayout blk) :vlax-true))
        (vlax-for obj blk
          (if (not inLayout)
            (if (blk0:fix obj)
              (setq count (1+ count))
              (setq failed (1+ failed))))
          (if (and (wcmatch (vla-get-ObjectName obj) "AcDbBlockReference,AcDbMInsertBlock")
                   (= (vla-get-HasAttributes obj) :vlax-true))
            (foreach att (vlax-invoke obj 'GetAttributes)
              (if (blk0:fix att)
                (setq count (1+ count))
                (setq failed (1+ failed)))))))))

  ;; Restore layer locks.
  (foreach lay locked
    (vla-put-Lock lay :vlax-true))

  (vla-Regen doc acAllViewports)
  (vla-EndUndoMark doc)

  (princ (strcat "\nChanged " (itoa count) " object(s) to layer 0 / ByLayer."))
  (if (> failed 0)
    (princ (strcat "\nCould not fully change " (itoa failed) " object(s).")))
  (princ))

(defun c:BlocksToLayer0 () (c:BLK0))

(princ "\nBlocksToLayer0 loaded. Type BLK0 to run.")
(princ)
